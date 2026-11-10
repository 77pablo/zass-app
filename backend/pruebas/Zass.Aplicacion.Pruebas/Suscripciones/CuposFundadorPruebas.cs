using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Suscripciones;

/// <summary>
/// MOD-13 - cupos del plan fundador (TBL-33). Los 20 primeros prestadores pagan precio de
/// lanzamiento: el cupo se reserva un rato mientras inscriben la tarjeta, se confirma al
/// quedar inscrita y se libera si cancelan o la reserva vence sin confirmarse.
///
/// Aqui se prueban las reglas con listas en memoria. Que la base entregue cada cupo a UNA
/// sola persona aunque lleguen treinta a la vez lo prueba integracion, contra PostgreSQL.
/// </summary>
public class CuposFundadorPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly AuditoriaEnMemoria _auditor = new();
    private readonly ColaEnMemoria _cola = new();
    private readonly ServicioSuscripciones _servicio;

    public CuposFundadorPruebas()
    {
        _servicio = new ServicioSuscripciones(
            new RepositorioSuscripcionesEnMemoria(_base), _base, _reloj, _auditor, _cola);
    }

    private PerfilPrestador UnPrestador()
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        _base.PerfilesPrestador.Add(perfil);
        return perfil;
    }

    private CupoFundador Cupo(short numero) => _base.CuposFundador.Single(c => c.Numero == numero);

    private Task<int> Disponibles() => _servicio.CuposFundadorDisponiblesAsync();

    // ------------------------------------------------------------ Estado inicial

    [Fact]
    public async Task Al_empezar_los_20_cupos_estan_disponibles()
    {
        Assert.Equal(20, await Disponibles());
    }

    // ------------------------------------------------------------ Reservar

    [Fact]
    public async Task Reservar_entrega_el_cupo_mas_bajo_y_baja_los_disponibles_a_19()
    {
        var p = UnPrestador();

        var r = await _servicio.ReservarCupoFundadorAsync(p.Id);

        Assert.True(r.Exito);
        Assert.Equal((short)1, r.Valor);
        Assert.Equal(19, await Disponibles());
        Assert.Equal(p.Id, Cupo(1).PerfilPrestadorId);
    }

    [Fact]
    public async Task La_reserva_dura_25_minutos_por_defecto()
    {
        var p = UnPrestador();
        var r = await _servicio.ReservarCupoFundadorAsync(p.Id);

        Assert.Equal(Ahora.AddMinutes(25), Cupo(r.Valor).ReservadoHasta);

        _reloj.Avanzar(TimeSpan.FromMinutes(24));
        Assert.Equal(19, await Disponibles());

        // Justo al cumplirse el plazo ya no esta guardado (reservado_hasta <= ahora).
        _reloj.Avanzar(TimeSpan.FromMinutes(1));
        Assert.Equal(20, await Disponibles());
    }

    [Fact]
    public async Task Reservar_dos_veces_con_el_mismo_perfil_devuelve_el_mismo_cupo_y_no_gasta_otro()
    {
        var p = UnPrestador();

        var primera = await _servicio.ReservarCupoFundadorAsync(p.Id);
        var segunda = await _servicio.ReservarCupoFundadorAsync(p.Id);

        Assert.True(segunda.Exito);
        Assert.Equal(primera.Valor, segunda.Valor);
        Assert.Equal(19, await Disponibles());
        Assert.Equal(1, _base.CuposFundador.Count(c => c.PerfilPrestadorId == p.Id));
    }

    [Fact]
    public async Task Reservar_de_nuevo_antes_de_confirmar_renueva_el_plazo()
    {
        var p = UnPrestador();
        var r = await _servicio.ReservarCupoFundadorAsync(p.Id);

        _reloj.Avanzar(TimeSpan.FromMinutes(20));
        await _servicio.ReservarCupoFundadorAsync(p.Id);

        Assert.Equal(Ahora.AddMinutes(45), Cupo(r.Valor).ReservadoHasta);
    }

    [Fact]
    public async Task Los_20_primeros_reciben_cupos_distintos_y_el_21_recibe_Conflicto()
    {
        var numeros = new List<short>();
        for (var i = 0; i < 20; i++)
        {
            var r = await _servicio.ReservarCupoFundadorAsync(UnPrestador().Id);
            Assert.True(r.Exito);
            numeros.Add(r.Valor);
        }

        Assert.Equal(Enumerable.Range(1, 20).Select(n => (short)n), numeros.OrderBy(n => n));
        Assert.Equal(0, await Disponibles());

        var registrosAntes = _auditor.Registros.Count;
        var vigesimoPrimero = await _servicio.ReservarCupoFundadorAsync(UnPrestador().Id);

        Assert.False(vigesimoPrimero.Exito);
        Assert.Equal(Fallas.Conflicto, vigesimoPrimero.Codigo);
        // Un intento sin cupo no deja rastro como si hubiera reservado algo.
        Assert.Equal(registrosAntes, _auditor.Registros.Count);
    }

    [Fact]
    public async Task Una_reserva_vencida_vuelve_a_estar_disponible_y_otra_persona_la_toma()
    {
        var a = UnPrestador();
        var b = UnPrestador();
        var deA = await _servicio.ReservarCupoFundadorAsync(a.Id);

        _reloj.Avanzar(TimeSpan.FromMinutes(26));
        Assert.Equal(20, await Disponibles());

        var deB = await _servicio.ReservarCupoFundadorAsync(b.Id);

        Assert.True(deB.Exito);
        Assert.Equal(deA.Valor, deB.Valor);
        Assert.Equal(b.Id, Cupo(deB.Valor).PerfilPrestadorId);

        // A volvio tarde: su cupo ya es de B, asi que recibe el siguiente.
        var deANuevo = await _servicio.ReservarCupoFundadorAsync(a.Id);
        Assert.Equal((short)2, deANuevo.Valor);
    }

    [Fact]
    public async Task Una_duracion_fuera_de_rango_se_rechaza_y_no_toca_ningun_cupo()
    {
        var p = UnPrestador();

        foreach (var minutos in new[] { 0, -5, 25 * 60 })
        {
            var r = await _servicio.ReservarCupoFundadorAsync(p.Id, TimeSpan.FromMinutes(minutos));
            Assert.False(r.Exito);
            Assert.Equal(Fallas.Validacion, r.Codigo);
        }

        Assert.Equal(20, await Disponibles());
        Assert.Empty(_auditor.Registros);
    }

    [Fact]
    public async Task Reservar_para_un_perfil_que_no_existe_da_NoEncontrado()
    {
        var r = await _servicio.ReservarCupoFundadorAsync(Guid.NewGuid());

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
        Assert.Equal(20, await Disponibles());
    }

    // ------------------------------------------------------------ Confirmar

    [Fact]
    public async Task Una_reserva_confirmada_no_vence_nunca()
    {
        var p = UnPrestador();
        var r = await _servicio.ReservarCupoFundadorAsync(p.Id);

        var confirmar = await _servicio.ConfirmarCupoFundadorAsync(p.Id);
        _reloj.Avanzar(TimeSpan.FromDays(7));
        await _servicio.CorrerDiarioAsync();

        Assert.True(confirmar.Exito);
        Assert.Equal(19, await Disponibles());
        Assert.Equal(p.Id, Cupo(r.Valor).PerfilPrestadorId);
        Assert.Null(Cupo(r.Valor).ReservadoHasta);
    }

    [Fact]
    public async Task Reservar_con_un_cupo_ya_confirmado_devuelve_el_mismo_y_no_le_pone_plazo()
    {
        var p = UnPrestador();
        var r = await _servicio.ReservarCupoFundadorAsync(p.Id);
        await _servicio.ConfirmarCupoFundadorAsync(p.Id);

        var otra = await _servicio.ReservarCupoFundadorAsync(p.Id);

        Assert.Equal(r.Valor, otra.Valor);
        Assert.Null(Cupo(r.Valor).ReservadoHasta);
    }

    [Fact]
    public async Task Confirmar_sin_tener_cupo_da_Conflicto()
    {
        var p = UnPrestador();

        var r = await _servicio.ConfirmarCupoFundadorAsync(p.Id);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.Conflicto, r.Codigo);
        Assert.Empty(_auditor.Registros);
    }

    // ------------------------------------------------------------ Liberar

    [Fact]
    public async Task Liberar_devuelve_el_cupo_y_sube_el_contador()
    {
        var p = UnPrestador();
        var r = await _servicio.ReservarCupoFundadorAsync(p.Id);
        await _servicio.ConfirmarCupoFundadorAsync(p.Id);
        Assert.Equal(19, await Disponibles());

        var liberar = await _servicio.LiberarCupoFundadorAsync(p.Id);

        Assert.True(liberar.Exito);
        Assert.Equal(20, await Disponibles());
        Assert.Null(Cupo(r.Valor).PerfilPrestadorId);
        Assert.Null(Cupo(r.Valor).ReservadoHasta);
    }

    [Fact]
    public async Task Liberar_sin_tener_cupo_no_es_error_y_no_deja_auditoria()
    {
        var p = UnPrestador();

        var r = await _servicio.LiberarCupoFundadorAsync(p.Id);

        Assert.True(r.Exito);
        Assert.Empty(_auditor.Registros);
        Assert.Equal(20, await Disponibles());
    }

    // ------------------------------------------------------------ Auditoria

    [Fact]
    public async Task Cada_cambio_de_cupo_queda_auditado_sobre_el_perfil()
    {
        var p = UnPrestador();

        await _servicio.ReservarCupoFundadorAsync(p.Id);
        await _servicio.ConfirmarCupoFundadorAsync(p.Id);
        await _servicio.LiberarCupoFundadorAsync(p.Id);
        await _servicio.LiberarCupoFundadorAsync(p.Id); // la segunda no hace nada

        Assert.Equal(
            new[] { "cupo_fundador_reservado", "cupo_fundador_confirmado", "cupo_fundador_liberado" },
            _auditor.Registros.Select(x => x.Accion).ToArray());
        Assert.All(_auditor.Registros, x => Assert.Equal(p.Id, x.EntidadId));
    }

    // ------------------------------------------------------------ Limpieza diaria

    [Fact]
    public async Task El_trabajo_diario_libera_las_reservas_vencidas_y_deja_las_confirmadas()
    {
        var sinConfirmar = UnPrestador();
        var confirmado = UnPrestador();
        var deSinConfirmar = await _servicio.ReservarCupoFundadorAsync(sinConfirmar.Id);
        var deConfirmado = await _servicio.ReservarCupoFundadorAsync(confirmado.Id);
        await _servicio.ConfirmarCupoFundadorAsync(confirmado.Id);

        _reloj.Avanzar(TimeSpan.FromHours(1));
        var resumen = await _servicio.CorrerDiarioAsync();

        // Se mira quien figura como dueno y no el contador de disponibles: el contador ya
        // cuenta las vencidas como libres, asi que no distinguiria si la limpieza corrio.
        Assert.Null(Cupo(deSinConfirmar.Valor).PerfilPrestadorId);
        Assert.Equal(confirmado.Id, Cupo(deConfirmado.Valor).PerfilPrestadorId);
        // Limpiar cupos no cambia lo que informa el trabajo diario.
        Assert.Equal(new ResumenDelDiario(0, 0), resumen);
    }
}