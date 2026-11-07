using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Comun;
using Zass.Dominio.Comun;
using Zass.Dominio.Credenciales;
using Zass.Dominio.Identidad;
using Zass.Infraestructura.Datos.Repositorios;
using Zass.Infraestructura.Seguridad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// MOD-02 contra PostgreSQL de verdad: lo que una lista en memoria no puede contestar.
/// Que los CHECK y el índice único rechacen de verdad, que borrar una credencial arrastre sus
/// documentos, y que "no catalogada" y "sin borradores" sean lo que la base devuelve.
/// </summary>
public class CredencialesIntegracionPruebas : IDisposable
{
    private readonly ContextoDePrueba _prueba = new();
    private readonly ServicioCredenciales _servicio;

    public CredencialesIntegracionPruebas()
    {
        var ctx = _prueba.Ctx;
        _servicio = new ServicioCredenciales(
            new RepositorioCredencialesEfCore(ctx), new UnidadDeTrabajoEfCore(ctx),
            new RelojUtc(), new AuditorEfCore(ctx));
    }

    public void Dispose() => _prueba.Dispose();

    private async Task<PerfilPrestador> UnPrestadorAsync()
    {
        var usuario = new Usuario
        {
            Correo = $"cred-{Guid.NewGuid():N}@zass.local", HashContrasena = "x",
            Nombre = "Prestador de prueba", CorreoVerificado = true,
        };
        _prueba.Ctx.Usuarios.Add(usuario);
        var perfil = new PerfilPrestador
        {
            UsuarioId = usuario.Id, NombrePublico = $"Estudio {Guid.NewGuid():N}",
            Slug = $"estudio-{Guid.NewGuid():N}", RubroId = 1,
        };
        _prueba.Ctx.PerfilesPrestador.Add(perfil);
        await _prueba.Ctx.SaveChangesAsync();
        return perfil;
    }

    private static CrearCredencialCmd Cmd(
        Guid perfil, string nombre = "Abogado", string? entidad = null) =>
        new(perfil, TipoCredencial.Titulo, nombre, null, entidad, new DateOnly(2020, 3, 15), null);

    private static string NombreUnico() => $"Entidad {Guid.NewGuid():N}";

    // ------------------------------------------------------------- guardar y leer

    [Fact]
    public async Task Una_credencial_creada_se_lee_de_vuelta_desde_la_base()
    {
        var perfil = await UnPrestadorAsync();
        var entidad = NombreUnico();

        var r = await _servicio.CrearAsync(Cmd(perfil.Id, "  Abogado  ", entidad));
        _prueba.Ctx.ChangeTracker.Clear();     // lo que se lee ahora sale de la base, no de memoria

        Assert.True(r.Exito);
        var mia = Assert.Single(await _servicio.MisCredencialesAsync(perfil.Id));
        Assert.Equal(r.Valor, mia.Id);
        Assert.Equal("Abogado", mia.Nombre);
        Assert.Equal(TipoCredencial.Titulo, mia.Tipo);
        Assert.Equal(EstadoCredencial.Borrador, mia.Estado);
        Assert.Equal(entidad, mia.Entidad);
        Assert.False(mia.EntidadReconocida);
        Assert.Equal(new DateOnly(2020, 3, 15), mia.ObtenidaEn);
    }

    [Fact]
    public async Task Una_entidad_escrita_dos_veces_con_distinta_capitalizacion_es_una_sola_fila()
    {
        var perfil = await UnPrestadorAsync();
        var nombre = NombreUnico();

        await _servicio.CrearAsync(Cmd(perfil.Id, "Una", nombre));
        await _servicio.CrearAsync(Cmd(perfil.Id, "Otra", nombre.ToUpperInvariant()));
        _prueba.Ctx.ChangeTracker.Clear();

        var filas = await _prueba.Ctx.EntidadesCertificadoras
            .Where(e => e.Nombre.ToLower() == nombre.ToLower()).CountAsync();
        Assert.Equal(1, filas);
        Assert.Equal(2, (await _servicio.MisCredencialesAsync(perfil.Id)).Count);
    }

    [Fact]
    public async Task El_indice_unico_impide_dos_entidades_con_el_mismo_nombre()
    {
        var nombre = NombreUnico();
        _prueba.Ctx.EntidadesCertificadoras.Add(new EntidadCertificadora { Nombre = nombre });
        await _prueba.Ctx.SaveChangesAsync();
        _prueba.Ctx.EntidadesCertificadoras.Add(new EntidadCertificadora { Nombre = nombre });

        var e = await Assert.ThrowsAsync<DbUpdateException>(() => _prueba.Ctx.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)e.InnerException!).SqlState);
    }

    // ------------------------------------------------------------- R-5 y R-6, en la base

    /// <summary>
    /// R-5: la regla "vence después de obtenerse" también vive en la base. Si alguien llamara al
    /// repositorio sin pasar por el servicio, la base seguiría rechazándolo.
    /// </summary>
    [Fact]
    public async Task La_base_rechaza_una_credencial_que_vence_antes_de_obtenerse()
    {
        var perfil = await UnPrestadorAsync();
        _prueba.Ctx.Credenciales.Add(new Credencial
        {
            PerfilPrestadorId = perfil.Id, Nombre = "Mala", Tipo = TipoCredencial.Curso,
            ObtenidaEn = new DateOnly(2020, 5, 1), VenceEn = new DateOnly(2020, 5, 1),
        });

        var e = await Assert.ThrowsAsync<DbUpdateException>(() => _prueba.Ctx.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)e.InnerException!).SqlState);
    }

    [Fact]
    public async Task La_base_rechaza_una_credencial_verificada_sin_verificador()
    {
        var perfil = await UnPrestadorAsync();
        _prueba.Ctx.Credenciales.Add(new Credencial
        {
            PerfilPrestadorId = perfil.Id, Nombre = "Sin sello de nadie", Tipo = TipoCredencial.Titulo,
            ObtenidaEn = new DateOnly(2020, 5, 1), Estado = EstadoCredencial.Verificada, SelloPor = null,
        });

        var e = await Assert.ThrowsAsync<DbUpdateException>(() => _prueba.Ctx.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)e.InnerException!).SqlState);
    }

    // ------------------------------------------------------------- eliminar

    [Fact]
    public async Task Eliminar_una_credencial_arrastra_sus_documentos()
    {
        var perfil = await UnPrestadorAsync();
        var id = (await _servicio.CrearAsync(Cmd(perfil.Id))).Valor;
        _prueba.Ctx.DocumentosCredencial.Add(new DocumentoCredencial
        {
            CredencialId = id, ClaveAlmacenamiento = "clave-aleatoria", NombreOriginal = "titulo.pdf",
            TipoMime = "application/pdf", TamanoBytes = 1000, Estado = EstadoDocumento.Disponible,
        });
        await _prueba.Ctx.SaveChangesAsync();
        _prueba.Ctx.ChangeTracker.Clear();

        var r = await _servicio.EliminarAsync(perfil.Id, id);
        _prueba.Ctx.ChangeTracker.Clear();

        Assert.True(r.Exito);
        Assert.False(await _prueba.Ctx.Credenciales.AnyAsync(c => c.Id == id));
        Assert.False(await _prueba.Ctx.DocumentosCredencial.AnyAsync(d => d.CredencialId == id));
    }

    [Fact]
    public async Task Eliminar_la_de_otro_prestador_no_borra_nada_en_la_base()
    {
        var yo = await UnPrestadorAsync();
        var otro = await UnPrestadorAsync();
        var deOtro = (await _servicio.CrearAsync(Cmd(otro.Id))).Valor;
        _prueba.Ctx.ChangeTracker.Clear();

        var r = await _servicio.EliminarAsync(yo.Id, deOtro);
        _prueba.Ctx.ChangeTracker.Clear();

        Assert.False(r.Exito);
        Assert.True(await _prueba.Ctx.Credenciales.AnyAsync(c => c.Id == deOtro));
    }

    // ------------------------------------------------------------- lo que ve el publico

    [Fact]
    public async Task El_publico_ve_las_credenciales_sin_borradores_y_con_la_etiqueta_correcta()
    {
        var perfil = await UnPrestadorAsync();
        await _servicio.CrearAsync(Cmd(perfil.Id, "Borrador secreto"));
        var enRevision = (await _servicio.CrearAsync(Cmd(perfil.Id, "En revision"))).Valor;
        var verificada = (await _servicio.CrearAsync(Cmd(perfil.Id, "Verificada"))).Valor;

        var c1 = await _prueba.Ctx.Credenciales.FindAsync(enRevision);
        c1!.Estado = EstadoCredencial.EnRevision;
        var c2 = await _prueba.Ctx.Credenciales.FindAsync(verificada);
        c2!.Verificar(Guid.NewGuid());
        await _prueba.Ctx.SaveChangesAsync();
        _prueba.Ctx.ChangeTracker.Clear();

        var publicas = await _servicio.CredencialesPublicasAsync(perfil.Id);

        Assert.Equal(new[] { "Verificada", "En revision" }, publicas.Select(p => p.Nombre).ToArray());
        Assert.Equal("Verificada", publicas[0].Etiqueta);
        Assert.Equal("Declarada por el usuario", publicas[1].Etiqueta);
        Assert.DoesNotContain(publicas, p => p.Nombre.Contains("secreto"));
        Assert.True(await _servicio.TieneVerificadasAsync(perfil.Id));
    }

    [Fact]
    public async Task Tiene_verificadas_es_falso_mientras_ninguna_tenga_sello()
    {
        var perfil = await UnPrestadorAsync();
        await _servicio.CrearAsync(Cmd(perfil.Id));

        Assert.False(await _servicio.TieneVerificadasAsync(perfil.Id));
    }

    /// <summary>
    /// El servicio vuelve a sacar los borradores, y eso taparia un repositorio que los trajera.
    /// Aqui se le pregunta al repositorio solo: lo que sale de la base ya no tiene borradores.
    /// </summary>
    [Fact]
    public async Task El_repositorio_por_si_solo_no_entrega_borradores_a_la_vista_publica()
    {
        var perfil = await UnPrestadorAsync();
        await _servicio.CrearAsync(Cmd(perfil.Id, "Borrador"));
        _prueba.Ctx.ChangeTracker.Clear();

        var repo = new RepositorioCredencialesEfCore(_prueba.Ctx);

        Assert.Empty(await repo.PublicasDePrestadorAsync(perfil.Id));
        Assert.Single(await repo.DePrestadorAsync(perfil.Id));
    }

    [Fact]
    public async Task Las_credenciales_de_un_prestador_no_incluyen_las_de_otro()
    {
        var yo = await UnPrestadorAsync();
        var otro = await UnPrestadorAsync();
        await _servicio.CrearAsync(Cmd(yo.Id, "Mia"));
        await _servicio.CrearAsync(Cmd(otro.Id, "De otro"));
        _prueba.Ctx.ChangeTracker.Clear();

        var mias = await _servicio.MisCredencialesAsync(yo.Id);

        Assert.Equal(new[] { "Mia" }, mias.Select(m => m.Nombre).ToArray());
    }

    [Fact]
    public async Task Que_otro_prestador_tenga_sello_no_da_sello_a_quien_no_lo_tiene()
    {
        var yo = await UnPrestadorAsync();
        var otro = await UnPrestadorAsync();
        var suya = (await _servicio.CrearAsync(Cmd(otro.Id))).Valor;
        (await _prueba.Ctx.Credenciales.FindAsync(suya))!.Verificar(Guid.NewGuid());
        await _prueba.Ctx.SaveChangesAsync();
        _prueba.Ctx.ChangeTracker.Clear();

        Assert.True(await _servicio.TieneVerificadasAsync(otro.Id));
        Assert.False(await _servicio.TieneVerificadasAsync(yo.Id));
    }

    [Fact]
    public async Task Una_entidad_apagada_no_se_puede_elegir_por_su_id()
    {
        var perfil = await UnPrestadorAsync();
        var apagada = new EntidadCertificadora { Nombre = NombreUnico(), Activa = false };
        _prueba.Ctx.EntidadesCertificadoras.Add(apagada);
        await _prueba.Ctx.SaveChangesAsync();

        var r = await _servicio.CrearAsync(new CrearCredencialCmd(
            perfil.Id, TipoCredencial.Titulo, "Abogado", apagada.Id, null, new DateOnly(2020, 3, 15), null));

        Assert.False(r.Exito);
    }

    [Fact]
    public async Task La_vista_publica_trae_el_nombre_de_la_entidad()
    {
        var perfil = await UnPrestadorAsync();
        var entidad = NombreUnico();
        var id = (await _servicio.CrearAsync(Cmd(perfil.Id, "Abogado", entidad))).Valor;
        (await _prueba.Ctx.Credenciales.FindAsync(id))!.Estado = EstadoCredencial.EnRevision;
        await _prueba.Ctx.SaveChangesAsync();
        _prueba.Ctx.ChangeTracker.Clear();

        var publica = Assert.Single(await _servicio.CredencialesPublicasAsync(perfil.Id));

        Assert.Equal(entidad, publica.Entidad);
    }

    [Fact]
    public async Task El_tope_de_credenciales_se_cuenta_en_la_base_por_prestador()
    {
        var perfil = await UnPrestadorAsync();
        var otro = await UnPrestadorAsync();
        for (var i = 0; i < ServicioCredenciales.MaximoPorPrestador; i++)
            Assert.True((await _servicio.CrearAsync(Cmd(perfil.Id, $"Credencial {i}"))).Exito);

        Assert.False((await _servicio.CrearAsync(Cmd(perfil.Id, "La que sobra"))).Exito);
        Assert.True((await _servicio.CrearAsync(Cmd(otro.Id, "De otro"))).Exito);
    }
}
