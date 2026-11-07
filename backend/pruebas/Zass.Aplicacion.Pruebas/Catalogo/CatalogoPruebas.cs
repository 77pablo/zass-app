using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Catalogo;

/// <summary>
/// MOD-03 - Catalogo. Rubros, servicios y lo que ofrece cada prestador (CU-03 paso 4).
/// </summary>
public class CatalogoPruebas
{
    private readonly BaseEnMemoria _base = new();
    private readonly RepositorioCatalogoEnMemoria _repo;
    private readonly AuditoriaEnMemoria _auditor = new();
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc));
    private readonly ServicioCatalogo _servicio;

    private const short Corretaje = 1;
    private const short Derecho = 2;

    public CatalogoPruebas()
    {
        _base.Rubros.Add(new Rubro { Id = Corretaje, Codigo = "corretaje", Nombre = "Corretaje", Activo = true });
        _base.Rubros.Add(new Rubro { Id = Derecho, Codigo = "derecho", Nombre = "Derecho", Activo = true });
        _base.Rubros.Add(new Rubro { Id = 9, Codigo = "jubilado", Nombre = "Rubro apagado", Activo = false });

        EnElCatalogo(101, Corretaje, "Tasación comercial");
        EnElCatalogo(102, Corretaje, "Administración de arriendos");
        EnElCatalogo(201, Derecho, "Escritura de compraventa");

        _repo = new RepositorioCatalogoEnMemoria(_base);
        _servicio = new ServicioCatalogo(_repo, _base, _reloj, _auditor);
    }

    /// <summary>
    /// Un servicio del catalogo con su clave de comparacion puesta, igual que lo deja la
    /// siembra del esquema. Escribirlo a mano sin la clave haria pasar pruebas que en la
    /// base real fallarian.
    /// </summary>
    private Servicio EnElCatalogo(int id, short rubroId, string nombre)
    {
        var s = new Servicio
        {
            Id = id,
            RubroId = rubroId,
            Nombre = nombre,
            NombreNormalizado = Texto.ClaveDeNombre(nombre),
            EsCatalogo = true,
        };
        _base.Servicios.Add(s);
        return s;
    }

    private Guid UnPrestador()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid(), RubroId = Corretaje };
        _base.PerfilesPrestador.Add(perfil);
        return perfil.Id;
    }

    private static ServicioOfrecidoCmd DelCatalogo(int id, short duracion = 60, string modo = "automatico") =>
        new(ServicioId: id, NombreNuevo: null, DuracionMinutos: duracion, ModoAgenda: modo, PrecioReferencia: null);

    private static ServicioOfrecidoCmd Nuevo(string nombre, short duracion = 60) =>
        new(ServicioId: null, NombreNuevo: nombre, DuracionMinutos: duracion, ModoAgenda: "manual", PrecioReferencia: null);

    private static ReemplazarServiciosCmd Cmd(Guid perfilId, params ServicioOfrecidoCmd[] servicios) =>
        new(perfilId, Corretaje, MaxServicios: 10, servicios);

    // ------------------------------------------------------------- API-02

    [Fact]
    public async Task BuscarRubros_no_devuelve_los_apagados()
    {
        var rubros = await _servicio.BuscarRubrosAsync();

        Assert.Equal(2, rubros.Count);
        Assert.DoesNotContain(rubros, r => r.Codigo == "jubilado");
    }

    [Fact]
    public async Task BuscarRubros_filtra_por_texto_sin_distinguir_mayusculas()
    {
        var rubros = await _servicio.BuscarRubrosAsync("CORRE");

        Assert.Single(rubros);
        Assert.Equal("corretaje", rubros[0].Codigo);
    }

    // ------------------------------------------------------------- API-03

    [Fact]
    public async Task ServiciosDeRubro_de_un_rubro_que_no_existe_no_encontrado()
    {
        var r = await _servicio.ServiciosDeRubroAsync(77);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    [Fact]
    public async Task ServiciosDeRubro_de_un_rubro_apagado_no_encontrado()
    {
        var r = await _servicio.ServiciosDeRubroAsync(9);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    /// <summary>
    /// Lo que un prestador agrego para si mismo no se le sugiere a los demas: el
    /// autocompletado solo ofrece el catalogo del equipo.
    /// </summary>
    [Fact]
    public async Task ServiciosDeRubro_no_sugiere_los_que_agrego_un_prestador()
    {
        _base.Servicios.Add(new Servicio
        {
            Id = 103, RubroId = Corretaje, Nombre = "Lo mio y de nadie mas",
            NombreNormalizado = "lo mio y de nadie mas", EsCatalogo = false,
        });

        var r = await _servicio.ServiciosDeRubroAsync(Corretaje);

        Assert.True(r.Exito);
        Assert.Equal(2, r.Valor!.Count);
        Assert.DoesNotContain(r.Valor!, s => s.Nombre == "Lo mio y de nadie mas");
    }

    // ------------------------------------------------------------- API-20

    [Fact]
    public async Task Reemplazar_guarda_la_oferta_del_catalogo()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarServiciosAsync(
            Cmd(perfilId, DelCatalogo(101, duracion: 45), DelCatalogo(102)));

        Assert.True(r.Exito, r.Mensaje);
        var oferta = await _servicio.ServiciosDePrestadorAsync(perfilId);
        Assert.Equal(2, oferta.Count);
        Assert.Contains(oferta, o => o.ServicioId == 101 && o.DuracionMinutos == 45 && o.Activo);
    }

    /// <summary>
    /// RN-S1-05 exige al menos un servicio para publicar. Dejar la lista vacia por
    /// API-20 apagaria un perfil publicado sin despublicarlo: quedaria "publicado" y
    /// no lo veria nadie, que es la peor forma de fallar.
    /// </summary>
    [Fact]
    public async Task Reemplazar_con_la_lista_vacia_se_rechaza()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarServiciosAsync(Cmd(perfilId));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.Contains("despublica", r.Mensaje!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reemplazar_respeta_el_tope_del_plan()
    {
        var perfilId = UnPrestador();
        var dos = new[] { DelCatalogo(101), DelCatalogo(102) };

        var r = await _servicio.ReemplazarServiciosAsync(
            new ReemplazarServiciosCmd(perfilId, Corretaje, MaxServicios: 1, dos));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.Contains("1", r.Mensaje!);
    }

    /// <summary>
    /// Un tope en cero es un plan que no se pudo leer, no un plan que permita cero
    /// servicios. El mensaje no puede mandar a la persona a revisar su suscripcion.
    /// </summary>
    [Fact]
    public async Task Reemplazar_con_el_tope_sin_determinar_no_culpa_al_plan()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarServiciosAsync(
            new ReemplazarServiciosCmd(perfilId, Corretaje, MaxServicios: 0,
                new[] { DelCatalogo(101) }));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.ReglaDeNegocio, r.Codigo);
        Assert.DoesNotContain("hasta 0", r.Mensaje!);
    }

    /// <summary>
    /// El unico de (perfil_prestador_id, servicio_id) lo rechazaria igual, pero con un
    /// 23505 convertido en 500. Se detecta antes de tocar la base.
    /// </summary>
    [Fact]
    public async Task Reemplazar_con_el_mismo_servicio_dos_veces_se_rechaza()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarServiciosAsync(
            Cmd(perfilId, DelCatalogo(101), DelCatalogo(101)));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Reemplazar_con_un_servicio_que_no_existe_se_rechaza()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarServiciosAsync(Cmd(perfilId, DelCatalogo(9999)));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Contains("9999", r.Mensaje!);
    }

    /// <summary>
    /// El rubro del perfil lo aporta quien compone (MOD-01). Ofrecer un servicio de otro
    /// rubro dejaria al prestador apareciendo en busquedas que no son la suya.
    /// </summary>
    [Fact]
    public async Task Reemplazar_con_un_servicio_de_otro_rubro_se_rechaza()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarServiciosAsync(Cmd(perfilId, DelCatalogo(201)));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
        Assert.Contains("rubro", r.Mensaje!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reemplazar_valida_los_servicios_en_una_sola_consulta()
    {
        var perfilId = UnPrestador();
        for (var i = 0; i < 8; i++)
            EnElCatalogo(300 + i, Corretaje, $"Servicio {i}");

        var muchos = Enumerable.Range(0, 8).Select(i => DelCatalogo(300 + i)).ToArray();
        var antes = _repo.ConsultasEnLote;

        var r = await _servicio.ReemplazarServiciosAsync(Cmd(perfilId, muchos));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(1, _repo.ConsultasEnLote - antes);
    }

    // ---------------------------------------------- servicios propios del prestador

    [Fact]
    public async Task Reemplazar_crea_el_servicio_nuevo_marcado_como_no_de_catalogo()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarServiciosAsync(
            Cmd(perfilId, Nuevo("Regularizacion de ampliaciones")));

        Assert.True(r.Exito, r.Mensaje);
        var creado = _base.Servicios.Single(s => s.Nombre == "Regularizacion de ampliaciones");
        Assert.False(creado.EsCatalogo);
        Assert.Equal(Corretaje, creado.RubroId);
    }

    /// <summary>
    /// "Tasacion " escrito a mano no puede crear un segundo servicio al lado del que ya
    /// esta en el catalogo: la busqueda por rubro acabaria con dos filas para lo mismo.
    /// </summary>
    [Fact]
    public async Task Reemplazar_reutiliza_el_servicio_que_ya_existia_con_ese_nombre()
    {
        var perfilId = UnPrestador();
        var cuantos = _base.Servicios.Count;

        // Sin tildes y con espacios de sobra: es el mismo servicio que "Tasación comercial".
        var r = await _servicio.ReemplazarServiciosAsync(
            Cmd(perfilId, Nuevo("  tasacion   comercial  ")));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(cuantos, _base.Servicios.Count);
        var oferta = await _servicio.ServiciosDePrestadorAsync(perfilId);
        Assert.Equal(101, oferta.Single().ServicioId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    public async Task Reemplazar_rechaza_un_nombre_nuevo_que_no_sirve(string nombre)
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarServiciosAsync(Cmd(perfilId, Nuevo(nombre)));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Reemplazar_rechaza_un_nombre_nuevo_demasiado_largo()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarServiciosAsync(Cmd(perfilId, Nuevo(new string('a', 121))));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Reemplazar_rechaza_pedir_el_catalogo_y_un_nombre_nuevo_a_la_vez()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarServiciosAsync(Cmd(perfilId,
            new ServicioOfrecidoCmd(101, "Otra cosa", 60, "manual", null)));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Reemplazar_rechaza_no_pedir_ninguno_de_los_dos()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarServiciosAsync(Cmd(perfilId,
            new ServicioOfrecidoCmd(null, null, 60, "manual", null)));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    // ------------------------------------------------------------- duracion y modo

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)5)]
    [InlineData((short)481)]
    public async Task Reemplazar_rechaza_una_duracion_fuera_de_rango(short duracion)
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarServiciosAsync(
            Cmd(perfilId, DelCatalogo(101, duracion: duracion)));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Reemplazar_rechaza_un_modo_de_agenda_desconocido()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarServiciosAsync(
            Cmd(perfilId, DelCatalogo(101, modo: "cuando se pueda")));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    [Fact]
    public async Task Reemplazar_rechaza_un_precio_negativo()
    {
        var perfilId = UnPrestador();

        var r = await _servicio.ReemplazarServiciosAsync(Cmd(perfilId,
            new ServicioOfrecidoCmd(101, null, 60, "automatico", -1)));

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Validacion, r.Codigo);
    }

    // ------------------------------------- reemplazar no es borrar y volver a crear

    /// <summary>
    /// TBL-18 `cita` y TBL-13 `disponibilidad` apuntan a `prestador_servicio` con
    /// borrado en cascada. Si reemplazar recreara las filas, cambiar el precio de un
    /// servicio se llevaria por delante las citas agendadas.
    /// </summary>
    [Fact]
    public async Task Reemplazar_conserva_el_identificador_de_la_oferta_que_sigue()
    {
        var perfilId = UnPrestador();
        await _servicio.ReemplazarServiciosAsync(Cmd(perfilId, DelCatalogo(101)));
        var antes = (await _servicio.ServiciosDePrestadorAsync(perfilId)).Single().Id;

        var r = await _servicio.ReemplazarServiciosAsync(
            Cmd(perfilId, DelCatalogo(101, duracion: 90), DelCatalogo(102)));

        Assert.True(r.Exito, r.Mensaje);
        var ahora = await _servicio.ServiciosDePrestadorAsync(perfilId);
        Assert.Equal(antes, ahora.Single(o => o.ServicioId == 101).Id);
        Assert.Equal(90, ahora.Single(o => o.ServicioId == 101).DuracionMinutos);
    }

    /// <summary>Lo que sale de la lista se desactiva. Nunca se borra: las citas se quedan.</summary>
    [Fact]
    public async Task Reemplazar_desactiva_lo_que_salio_en_vez_de_borrarlo()
    {
        var perfilId = UnPrestador();
        await _servicio.ReemplazarServiciosAsync(Cmd(perfilId, DelCatalogo(101), DelCatalogo(102)));

        var r = await _servicio.ReemplazarServiciosAsync(Cmd(perfilId, DelCatalogo(101)));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(2, _base.PrestadorServicios.Count(o => o.PerfilPrestadorId == perfilId));
        Assert.False(_base.PrestadorServicios.Single(o => o.ServicioId == 102).Activo);
    }

    [Fact]
    public async Task Reemplazar_reactiva_el_servicio_que_vuelve_a_la_lista()
    {
        var perfilId = UnPrestador();
        await _servicio.ReemplazarServiciosAsync(Cmd(perfilId, DelCatalogo(101), DelCatalogo(102)));
        await _servicio.ReemplazarServiciosAsync(Cmd(perfilId, DelCatalogo(101)));

        var r = await _servicio.ReemplazarServiciosAsync(Cmd(perfilId, DelCatalogo(101), DelCatalogo(102)));

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(2, _base.PrestadorServicios.Count(o => o.PerfilPrestadorId == perfilId));
        Assert.True(_base.PrestadorServicios.Single(o => o.ServicioId == 102).Activo);
    }

    /// <summary>
    /// El tope del plan cuenta lo que se ofrece, no el historial: un prestador que
    /// probo y descarto nueve servicios no puede quedarse sin poder ofrecer el decimo.
    /// </summary>
    [Fact]
    public async Task El_tope_cuenta_los_activos_no_las_filas_desactivadas()
    {
        var perfilId = UnPrestador();
        var dos = new[] { DelCatalogo(101), DelCatalogo(102) };
        await _servicio.ReemplazarServiciosAsync(
            new ReemplazarServiciosCmd(perfilId, Corretaje, MaxServicios: 2, dos));

        var r = await _servicio.ReemplazarServiciosAsync(
            new ReemplazarServiciosCmd(perfilId, Corretaje, MaxServicios: 2, new[] { DelCatalogo(101) }));

        Assert.True(r.Exito, r.Mensaje);
    }

    [Fact]
    public async Task Reemplazar_no_toca_la_oferta_de_otro_prestador()
    {
        var unoId = UnPrestador();
        var otroId = UnPrestador();
        await _servicio.ReemplazarServiciosAsync(Cmd(otroId, DelCatalogo(101), DelCatalogo(102)));

        await _servicio.ReemplazarServiciosAsync(Cmd(unoId, DelCatalogo(101)));

        var deOtro = await _servicio.ServiciosDePrestadorAsync(otroId);
        Assert.Equal(2, deOtro.Count(o => o.Activo));
    }

    [Fact]
    public async Task Reemplazar_que_falla_no_deja_nada_a_medias()
    {
        var perfilId = UnPrestador();
        var guardadosAntes = _base.Guardados;

        var r = await _servicio.ReemplazarServiciosAsync(
            Cmd(perfilId, DelCatalogo(101), DelCatalogo(9999)));

        Assert.False(r.Exito);
        Assert.Empty(_base.PrestadorServicios);
        Assert.Equal(guardadosAntes, _base.Guardados);
    }

    // ------------------------------------------------- lo que MOD-06 va a necesitar

    [Fact]
    public async Task DatosDePrestadorServicio_devuelve_duracion_y_modo()
    {
        var perfilId = UnPrestador();
        await _servicio.ReemplazarServiciosAsync(
            Cmd(perfilId, DelCatalogo(101, duracion: 30, modo: "manual")));
        var ofertaId = (await _servicio.ServiciosDePrestadorAsync(perfilId)).Single().Id;

        var r = await _servicio.DatosDePrestadorServicioAsync(ofertaId);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(30, r.Valor!.DuracionMinutos);
        Assert.Equal("Manual", r.Valor!.ModoAgenda);
        Assert.Equal("Tasación comercial", r.Valor!.Nombre);
    }

    [Fact]
    public async Task DatosDePrestadorServicio_de_algo_que_no_existe_no_encontrado()
    {
        var r = await _servicio.DatosDePrestadorServicioAsync(Guid.NewGuid());

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
    }

    [Fact]
    public async Task Reemplazar_queda_en_la_auditoria()
    {
        var perfilId = UnPrestador();

        await _servicio.ReemplazarServiciosAsync(Cmd(perfilId, DelCatalogo(101)));

        Assert.Contains(_auditor.Registros, a => a.Accion == "reemplazar_servicios");
    }
}
