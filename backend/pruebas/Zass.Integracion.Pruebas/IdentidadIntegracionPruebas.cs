using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zass.Aplicacion.Identidad;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// MOD-01 contra PostgreSQL de verdad. Las pruebas de aplicacion ya cubren las reglas;
/// estas cubren lo que solo la base puede contestar: si los indices unicos rechazan, si
/// la columna generada se llena, si el token de concurrencia muerde.
/// </summary>
public class IdentidadIntegracionPruebas : IDisposable
{
    private readonly ContextoDePrueba _prueba = new();
    private readonly RelojUtc _reloj = new();
    private readonly HashArgon2id _hash = new();
    private readonly GeneradorSecretos _secretos = new();
    private readonly CifradoAesGcm _cifrado = new(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    private readonly ServicioRegistro _registro;
    private readonly ServicioSesion _sesion;
    private readonly ServicioCuenta _cuenta;
    private readonly ServicioPerfiles _perfiles;
    private readonly RepositorioSesionesEfCore _repoSesiones;

    /// <summary>
    /// Correo y nombre unicos por prueba. El rollback aisla los datos, pero dos pruebas
    /// insertando el mismo correo a la vez se bloquearian entre si en el indice unico.
    /// </summary>
    private readonly string _correo = $"prueba-{Guid.NewGuid():N}@zass.local";
    private readonly string _nombre = $"Prestador {Guid.NewGuid():N}";

    private const string Clave = "contrasena-larga-1";

    public IdentidadIntegracionPruebas()
    {
        var ctx = _prueba.Ctx;
        var usuarios = new RepositorioUsuariosEfCore(ctx);
        var perfiles = new RepositorioPerfilesEfCore(ctx);
        _repoSesiones = new RepositorioSesionesEfCore(ctx);
        var unidad = new UnidadDeTrabajoEfCore(ctx);
        var cola = new ColaNotificacionesEfCore(ctx);
        var auditor = new AuditorEfCore(ctx);

        _registro = new ServicioRegistro(
            usuarios, perfiles, unidad, _hash, _secretos, _reloj, cola, auditor);
        _sesion = new ServicioSesion(
            usuarios, _repoSesiones, unidad, _hash, _secretos, _reloj, auditor);
        _cuenta = new ServicioCuenta(
            usuarios, perfiles, unidad, _hash, _secretos, _reloj, cola, auditor, _sesion);
        _perfiles = new ServicioPerfiles(
            usuarios, perfiles, unidad, _reloj, _cifrado, cola, auditor);
    }

    public void Dispose() => _prueba.Dispose();

    // ------------------------------------------------------------------------

    [Fact]
    public async Task Los_cinco_roles_de_catalogo_estan_en_la_base()
    {
        // Sin esto ningun registro puede completarse, porque el servicio busca el rol
        // por codigo antes de crear la cuenta.
        var roles = await _prueba.Ctx.Roles.OrderBy(r => r.Id).ToListAsync();

        Assert.Equal(
            new[] { "buscador", "prestador", "verificador", "moderador", "administrador" },
            roles.Select(r => r.Codigo).ToArray());
        Assert.Equal(new[] { false, false, true, true, true }, roles.Select(r => r.EsInterno).ToArray());
    }

    [Fact]
    public async Task La_cadena_completa_funciona_contra_la_base_real()
    {
        // Registro -> verificar correo -> iniciar sesion -> editar perfil -> publicar.
        var alta = await _registro.RegistrarAsync(
            new RegistrarCuentaCmd(_correo, Clave, _nombre, "prestador"));
        Assert.True(alta.Exito, alta.Mensaje);

        var token = await TokenDeVerificacionAsync(alta.Valor!.UsuarioId);
        Assert.True((await _cuenta.VerificarCorreoAsync(token)).Exito);

        var entrada = await _sesion.IniciarAsync(new IniciarSesionCmd(_correo, Clave));
        Assert.True(entrada.Exito, entrada.Mensaje);
        Assert.Equal(new[] { "prestador" }, entrada.Valor!.Roles);

        var usuarioId = alta.Valor!.UsuarioId;
        var perfil = await _prueba.Ctx.PerfilesPrestador.SingleAsync(p => p.UsuarioId == usuarioId);
        await CompletarMinimoPublicableAsync(perfil);

        var edicion = await _perfiles.EditarPrestadorAsync(usuarioId, new EditarPerfilPrestadorCmd(
            NombrePublico: _nombre, Descripcion: "Derecho de familia", RubroId: perfil.RubroId,
            TelefonoPublico: "+56912345678", CorreoPublico: null, Whatsapp: null, SitioWeb: null,
            Direccion: "Avenida Alemania 0231", Latitud: -38.74, Longitud: -72.60,
            Version: perfil.Version));
        Assert.True(edicion.Exito, edicion.Mensaje);

        var publicacion = await _perfiles.PublicarAsync(usuarioId);
        Assert.True(publicacion.Exito, publicacion.Mensaje);

        // Lo que importa: leido de la base, no del objeto que quedo en memoria.
        _prueba.Ctx.ChangeTracker.Clear();
        var guardado = await _prueba.Ctx.PerfilesPrestador.SingleAsync(p => p.UsuarioId == usuarioId);
        Assert.Equal(EstadoPerfil.Publicado, guardado.Estado);
        Assert.NotNull(guardado.PublicadoEn);
        Assert.Equal("Avenida Alemania 0231", _cifrado.Descifrar(guardado.DireccionCifrada!));

        var usuario = await _prueba.Ctx.Usuarios.SingleAsync(u => u.Id == usuarioId);
        Assert.True(usuario.CorreoVerificado);
        Assert.Equal(EstadoCuenta.Activa, usuario.Estado);
        Assert.StartsWith("$argon2id$", usuario.HashContrasena);
    }

    [Fact]
    public async Task El_indice_unico_rechaza_el_correo_repetido_aunque_el_servicio_lo_deje_pasar()
    {
        // La comprobacion de ExisteCorreoAsync es por cortesia: dos registros simultaneos
        // la pasan los dos. Lo que de verdad impide el duplicado es este indice, y hasta
        // ahora nadie lo habia comprobado.
        var alta = await _registro.RegistrarAsync(
            new RegistrarCuentaCmd(_correo, Clave, _nombre, "buscador"));
        Assert.True(alta.Exito, alta.Mensaje);

        _prueba.Ctx.Usuarios.Add(new Usuario
        {
            Correo = _correo,   // mismo correo, saltandose el servicio
            Nombre = "Colado",
            HashContrasena = _hash.Hash(Clave),
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(
            () => _prueba.Ctx.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal("23505", pg.SqlState);   // unique_violation
    }

    [Fact]
    public async Task El_correo_no_distingue_mayusculas_porque_la_columna_es_citext()
    {
        var alta = await _registro.RegistrarAsync(
            new RegistrarCuentaCmd(_correo, Clave, _nombre, "buscador"));
        Assert.True(alta.Exito, alta.Mensaje);

        // Buscado en MAYUSCULAS contra una fila guardada en minusculas: lo resuelve la
        // base, no el codigo.
        var encontrado = await _prueba.Ctx.Usuarios
            .FirstOrDefaultAsync(u => u.Correo == _correo.ToUpperInvariant());

        Assert.NotNull(encontrado);
    }

    [Fact]
    public async Task La_columna_geography_se_llena_sola_desde_latitud_y_longitud()
    {
        var usuarioId = await PrestadorConPerfilAsync();
        var perfil = await _prueba.Ctx.PerfilesPrestador.SingleAsync(p => p.UsuarioId == usuarioId);

        perfil.Latitud = -38.7359;
        perfil.Longitud = -72.5904;
        await _prueba.Ctx.SaveChangesAsync();

        var punto = await EscalarAsync(
            "SELECT ST_AsText(ubicacion_exacta::geometry) FROM perfiles_prestador WHERE id = @id",
            perfil.Id);

        // PostGIS guarda longitud primero. Que esto pase prueba que la columna generada
        // no se quedo nula, que es lo que haria que la busqueda por cercania no
        // encontrara a nadie.
        Assert.StartsWith("POINT(-72.5904 -38.7359", punto);
    }

    [Fact]
    public async Task Sin_coordenadas_la_columna_geography_queda_nula_y_no_revienta()
    {
        var usuarioId = await PrestadorConPerfilAsync();
        var perfil = await _prueba.Ctx.PerfilesPrestador.SingleAsync(p => p.UsuarioId == usuarioId);

        var punto = await EscalarAsync(
            "SELECT ST_AsText(ubicacion_exacta::geometry) FROM perfiles_prestador WHERE id = @id",
            perfil.Id);

        Assert.Null(punto);
    }

    [Fact]
    public async Task La_version_optimista_la_hace_cumplir_la_base_y_no_solo_el_servicio()
    {
        // CC-02. El servicio compara versiones antes de escribir, pero esa comparacion
        // no sirve si dos peticiones llegan juntas. Aqui se mueve la version por detras
        // de EF Core, que es exactamente lo que hace el otro aparato.
        var usuarioId = await PrestadorConPerfilAsync();
        var perfil = await _prueba.Ctx.PerfilesPrestador.SingleAsync(p => p.UsuarioId == usuarioId);

        await _prueba.Ctx.Database.ExecuteSqlRawAsync(
            "UPDATE perfiles_prestador SET version = version + 1 WHERE id = {0}", perfil.Id);

        perfil.NombrePublico = "Pisado por el otro aparato";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => _prueba.Ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task El_filtro_de_sesiones_vigentes_en_SQL_dice_lo_mismo_que_el_metodo_del_dominio()
    {
        // VigentesDeUsuarioAsync reescribe en SQL la regla de Sesion.EstaVigente porque
        // ese metodo no se traduce. Son dos escrituras de la misma regla: si una cambia
        // y la otra no, esta prueba se cae.
        var alta = await _registro.RegistrarAsync(
            new RegistrarCuentaCmd(_correo, Clave, _nombre, "buscador"));
        var usuarioId = alta.Valor!.UsuarioId;
        var ahora = _reloj.Ahora;

        _repoSesiones.Agregar(Sesion(usuarioId, ahora.AddDays(30), null));      // vigente
        _repoSesiones.Agregar(Sesion(usuarioId, ahora.AddDays(30), ahora));     // revocada
        _repoSesiones.Agregar(Sesion(usuarioId, ahora.AddMinutes(-1), null));   // vencida
        await _prueba.Ctx.SaveChangesAsync();

        var porSql = await _repoSesiones.VigentesDeUsuarioAsync(usuarioId, ahora);
        var todas = await _prueba.Ctx.Sesiones.Where(s => s.UsuarioId == usuarioId).ToListAsync();
        var porDominio = todas.Where(s => s.EstaVigente(ahora)).ToList();

        Assert.Single(porSql);
        Assert.Equal(
            porDominio.Select(s => s.Id).OrderBy(x => x),
            porSql.Select(s => s.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task El_aviso_de_verificacion_y_la_auditoria_quedan_guardados_de_verdad()
    {
        var alta = await _registro.RegistrarAsync(
            new RegistrarCuentaCmd(_correo, Clave, _nombre, "buscador"));
        var usuarioId = alta.Valor!.UsuarioId;

        var aviso = await _prueba.Ctx.Notificaciones
            .SingleAsync(n => n.DestinatarioId == usuarioId);
        Assert.Equal(TipoNotificacion.CorreoVerificacion, aviso.Tipo);
        Assert.Equal(EstadoEnvio.Programada, aviso.Estado);

        var entrada = await _prueba.Ctx.Auditoria
            .SingleAsync(a => a.EntidadId == usuarioId && a.Accion == "registro_cuenta");
        Assert.DoesNotContain(Clave, entrada.Contexto ?? "");
    }

    [Fact]
    public async Task Eliminar_la_cuenta_deja_la_fila_anonimizada_en_la_base()
    {
        // RN-S1-07 comprobado donde importa: la fila sigue ahi, sin datos personales.
        var alta = await _registro.RegistrarAsync(
            new RegistrarCuentaCmd(_correo, Clave, _nombre, "buscador"));
        var usuarioId = alta.Valor!.UsuarioId;

        Assert.True((await _cuenta.EliminarAsync(usuarioId)).Exito);

        _prueba.Ctx.ChangeTracker.Clear();
        var usuario = await _prueba.Ctx.Usuarios.SingleAsync(u => u.Id == usuarioId);
        Assert.Equal(EstadoCuenta.Eliminada, usuario.Estado);
        Assert.DoesNotContain(_correo, usuario.Correo);
        Assert.Equal("", usuario.HashContrasena);
        Assert.NotNull(usuario.AnonimizadoEn);
    }

    // ------------------------------------------------------------- auxiliares

    private static Sesion Sesion(Guid usuarioId, DateTime expira, DateTime? revocada) => new()
    {
        UsuarioId = usuarioId,
        HashToken = Guid.NewGuid().ToString("N"),
        ExpiraEn = expira,
        RevocadaEn = revocada,
    };

    private async Task<Guid> PrestadorConPerfilAsync()
    {
        var alta = await _registro.RegistrarAsync(
            new RegistrarCuentaCmd(_correo, Clave, _nombre, "prestador"));
        Assert.True(alta.Exito, alta.Mensaje);
        return alta.Valor!.UsuarioId;
    }

    /// <summary>Lo que pondran MOD-03 y MOD-04: un rubro, un servicio activo y una zona.</summary>
    private async Task CompletarMinimoPublicableAsync(PerfilPrestador perfil)
    {
        var rubro = await _prueba.Ctx.Rubros.FirstOrDefaultAsync();
        if (rubro is null)
        {
            rubro = new Rubro { Id = 1, Codigo = "legal", Nombre = "Legal" };
            _prueba.Ctx.Rubros.Add(rubro);
        }

        var servicio = await _prueba.Ctx.Servicios.FirstOrDefaultAsync(s => s.RubroId == rubro.Id);
        if (servicio is null)
        {
            servicio = new Servicio { RubroId = rubro.Id, Nombre = "Asesoria" };
            _prueba.Ctx.Servicios.Add(servicio);
        }

        var zona = await _prueba.Ctx.Zonas.FirstOrDefaultAsync();
        if (zona is null)
        {
            zona = new Zona
            {
                Nombre = "Amanecer", Sector = "Norte",
                CentroLatitud = -38.72, CentroLongitud = -72.61,
            };
            _prueba.Ctx.Zonas.Add(zona);
        }

        await _prueba.Ctx.SaveChangesAsync();

        perfil.RubroId = rubro.Id;
        _prueba.Ctx.PrestadorServicios.Add(new PrestadorServicio
        {
            PerfilPrestadorId = perfil.Id, ServicioId = servicio.Id, Activo = true,
        });
        _prueba.Ctx.Coberturas.Add(new CoberturaPrestador
        {
            PerfilPrestadorId = perfil.Id, ZonaId = zona.Id, EsPrincipal = true,
        });
        await _prueba.Ctx.SaveChangesAsync();
        _prueba.Ctx.ChangeTracker.Clear();
    }

    private async Task<string> TokenDeVerificacionAsync(Guid usuarioId)
    {
        // El token en claro solo existe en el cuerpo del aviso: en la tabla usuario esta
        // el hash. Esto es, literalmente, lo que haria la persona al abrir su correo.
        var aviso = await _prueba.Ctx.Notificaciones
            .Where(n => n.DestinatarioId == usuarioId
                     && n.Tipo == TipoNotificacion.CorreoVerificacion)
            .OrderByDescending(n => n.ProgramadaPara)
            .FirstAsync();

        return Zass.Aplicacion.Puertos.CargaDeAviso.LeerToken(aviso.Cuerpo)!;
    }

    private async Task<string?> EscalarAsync(string sql, Guid id)
    {
        var conexion = _prueba.Ctx.Database.GetDbConnection();
        using var orden = conexion.CreateCommand();
        orden.CommandText = sql;
        orden.Transaction = _prueba.Ctx.Database.CurrentTransaction?.GetDbTransaction();

        var parametro = orden.CreateParameter();
        parametro.ParameterName = "id";
        parametro.Value = id;
        orden.Parameters.Add(parametro);

        var valor = await orden.ExecuteScalarAsync();
        return valor is null or DBNull ? null : (string)valor;
    }
}
