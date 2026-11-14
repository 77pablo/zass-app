using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Suscripciones;

/// <summary>
/// MOD-13 · S7. Lo que queda escrito en la auditoria del cobro, y que hasta hoy no vigilaba
/// nadie.
///
/// <para>
/// Por que existe este archivo: cuatro entradas de <c>ServicioCobros</c> decian que la
/// entidad afectada era <c>intentos_pago</c>, y esa tabla NO EXISTE — la tabla es
/// <c>intentos_de_pago</c>. Compilaba, las 36 pruebas del cobro pasaban, y el error solo se
/// habria notado el dia que alguien tuviera que explicar un cobro: la consulta de auditoria
/// por tabla no habria devuelto ni una fila, y la conclusion natural seria «no hay rastro de
/// ese cobro» en vez de «la etiqueta esta mal escrita». Una auditoria que miente es peor que
/// no tenerla, porque se le cree.
/// </para>
///
/// <para>
/// Lo que vigilan estas pruebas, y ninguna otra: que cada entrada del cobro nombre una tabla
/// que existe, y que el identificador que lleva sea de la tabla que dice ser. Los nombres van
/// escritos a mano, uno por uno: una prueba que saca la lista de tablas del mismo codigo que
/// vigila no vigila nada.
/// </para>
/// </summary>
public class AuditoriaDeCobrosPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Las tablas de S7, escritas a mano. Si alguna se renombra, esta lista tiene que
    /// cambiar con ella, y eso es exactamente lo que se quiere: que renombrar una tabla
    /// obligue a mirar la auditoria.
    /// </summary>
    private static readonly string[] TablasDeS7 =
    {
        "planes", "suscripciones", "pagos",
        "cupos_fundador", "cupones", "canjes_de_cupon", "intentos_de_pago",
        // No es de S7, pero el cupo fundador audita sobre el perfil.
        "perfiles_prestador",
    };

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly PasarelaFalsa _pasarela = new();
    private readonly AuditoriaEnMemoria _auditor = new();
    private readonly ServicioSuscripciones _suscripciones;
    private readonly ServicioCobros _cobros;

    public AuditoriaDeCobrosPruebas()
    {
        var repo = new RepositorioSuscripcionesEnMemoria(_base);
        _suscripciones = new ServicioSuscripciones(
            repo, _base, _reloj, new AuditoriaEnMemoria(), new ColaEnMemoria());
        // El MISMO auditor que mira la prueba. Los demas archivos del cobro le pasan uno
        // de usar y tirar, y por eso ninguno podia ver lo que escribe.
        _cobros = new ServicioCobros(
            repo, _suscripciones, _pasarela, _base, _reloj, _auditor);

        _base.Planes.Add(new Plan { Id = 1, Codigo = "prueba", Nombre = "ZASS! Prueba", PrecioMensual = 0 });
        _base.Planes.Add(new Plan { Id = 2, Codigo = "mensual", Nombre = "ZASS! Mensual", PrecioMensual = 7990 });
        _base.Planes.Add(new Plan { Id = 3, Codigo = "fundador", Nombre = "ZASS! Fundador", PrecioMensual = 5990 });
    }

    // ------------------------------------------------------------------- Montaje

    private static AvisoDePasarela UnAviso() => new(new Dictionary<string, string>());

    private PerfilPrestador UnPrestadorConSuscripcion()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);
        _suscripciones.IniciarPruebaAsync(perfil.Id).GetAwaiter().GetResult();
        return perfil;
    }

    /// <summary>
    /// Recorre los cinco caminos que auditan: cobro iniciado, cobro confirmado, monto que
    /// no coincide, tarjeta rechazada e inscripcion del fundador. Se hace en una sola
    /// prueba a proposito: lo que se vigila es el conjunto de lo que queda escrito, no
    /// cada camino por separado — esos ya tienen sus propias pruebas.
    /// </summary>
    private async Task CorrerLosCaminosQueAuditan()
    {
        // 1 y 2 · Un cobro que sale bien, de punta a punta.
        var uno = UnPrestadorConSuscripcion();
        var r = await _cobros.IniciarAsync(new IniciarCobroCmd(uno.Id, "mensual", null, "k-ok"));
        Assert.True(r.Exito, "El montaje fallo al iniciar: " + r.Mensaje);
        var intentoUno = _base.IntentosDePago.Single(i => i.PerfilPrestadorId == uno.Id);
        _pasarela.Veredicto = new CobroResuelto(intentoUno.Id, "ref-ok", true, intentoUno.Monto, null);
        Assert.True((await _cobros.ConfirmarAsync(UnAviso())).Exito);

        // 3 · Un monto que no coincide.
        var dos = UnPrestadorConSuscripcion();
        Assert.True((await _cobros.IniciarAsync(
            new IniciarCobroCmd(dos.Id, "mensual", null, "k-monto"))).Exito);
        var intentoDos = _base.IntentosDePago.Single(i => i.PerfilPrestadorId == dos.Id);
        _pasarela.Veredicto = new CobroResuelto(intentoDos.Id, "ref-monto", true, 1, null);
        Assert.False((await _cobros.ConfirmarAsync(UnAviso())).Exito);

        // 4 · Una tarjeta rechazada, que es un desenlace normal (RN-S7-10).
        var tres = UnPrestadorConSuscripcion();
        Assert.True((await _cobros.IniciarAsync(
            new IniciarCobroCmd(tres.Id, "mensual", null, "k-rechazo"))).Exito);
        var intentoTres = _base.IntentosDePago.Single(i => i.PerfilPrestadorId == tres.Id);
        _pasarela.Veredicto = new CobroResuelto(intentoTres.Id, "ref-rechazo", false, 0, "rechazada");
        Assert.True((await _cobros.ConfirmarAsync(UnAviso())).Exito);

        // 5 · La inscripcion del cupo fundador, con su cobro de cero pesos.
        var cuatro = UnPrestadorConSuscripcion();
        Assert.True((await _cobros.IniciarInscripcionFundadorAsync(cuatro.Id, "k-ins")).Exito);
        var intentoCuatro = _base.IntentosDePago.Single(i => i.PerfilPrestadorId == cuatro.Id);
        _pasarela.Veredicto = new CobroResuelto(intentoCuatro.Id, "ref-ins", true, 0, null);
        Assert.True((await _cobros.ConfirmarAsync(UnAviso())).Exito);
    }

    // --------------------------------------------- La etiqueta nombra una tabla que existe

    [Fact]
    public async Task Cada_entrada_de_auditoria_del_cobro_nombra_una_tabla_que_EXISTE()
    {
        await CorrerLosCaminosQueAuditan();

        Assert.NotEmpty(_auditor.Registros);
        var inventadas = _auditor.Registros
            .Where(e => !TablasDeS7.Contains(e.Entidad))
            .Select(e => $"{e.Accion} -> «{e.Entidad}»")
            .Distinct()
            .ToArray();

        Assert.True(
            inventadas.Length == 0,
            "Estas entradas nombran una tabla que no existe: " + string.Join(", ", inventadas));
    }

    /// <summary>
    /// El candado con literal. La prueba de arriba se contentaria con que la etiqueta este
    /// en la lista; esta exige el nombre exacto, por si algun dia alguien «arregla» la
    /// lista en vez del codigo.
    /// </summary>
    [Fact]
    public async Task La_tabla_de_los_intentos_se_llama_intentos_de_pago_y_no_intentos_pago()
    {
        await CorrerLosCaminosQueAuditan();

        Assert.DoesNotContain("intentos_pago", _auditor.Registros.Select(e => e.Entidad));
        Assert.Contains("intentos_de_pago", _auditor.Registros.Select(e => e.Entidad));
    }

    // ------------------------------------------- El identificador es de la tabla que dice

    /// <summary>
    /// Lo que la comprobacion del nombre no ve: una entrada bien escrita que apunta a otra
    /// cosa. Decir «intentos_de_pago» con el id de una suscripcion es la misma mentira,
    /// solo mejor escrita, y la consulta de auditoria devolveria cero filas igual.
    /// </summary>
    [Fact]
    public async Task El_identificador_de_cada_entrada_es_de_la_tabla_que_dice_ser()
    {
        await CorrerLosCaminosQueAuditan();

        var intentos = _base.IntentosDePago.Select(i => i.Id).ToHashSet();
        var suscripciones = _base.Suscripciones.Select(s => s.Id).ToHashSet();
        var perfiles = _base.PerfilesPrestador.Select(p => p.Id).ToHashSet();

        foreach (var e in _auditor.Registros)
        {
            Assert.NotNull(e.EntidadId);
            var existe = e.Entidad switch
            {
                "intentos_de_pago" => intentos.Contains(e.EntidadId!.Value),
                "suscripciones" => suscripciones.Contains(e.EntidadId!.Value),
                "perfiles_prestador" => perfiles.Contains(e.EntidadId!.Value),
                "pagos" => _base.Pagos.Any(p => p.Id == e.EntidadId!.Value),
                _ => false,
            };
            Assert.True(
                existe,
                $"La entrada «{e.Accion}» dice ser de «{e.Entidad}» y su id no esta en esa tabla.");
        }
    }
}
