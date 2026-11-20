using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Tarjetas;
using Zass.Dominio.Comun;
using Zass.Dominio.Tarjetas;

namespace Zass.Aplicacion.Pruebas.Tarjetas;

/// <summary>
/// API-56 a API-58 (CU-20, CS-18): lo que el dueno hace con sus tarjetas.
///
/// El doble busca por id SIN filtrar por dueno, igual que la consulta real: la comprobacion de
/// dueno vive en el servicio, y estas pruebas son las que la ven caer.
/// </summary>
public class MisTarjetasPruebas
{
    private static readonly DateTime Ahora = new(2026, 11, 14, 18, 0, 0, DateTimeKind.Utc);

    private readonly RepositorioTarjetasEnMemoria _repo = new();
    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly SecretosDeterministas _secretos = new();
    private readonly AuditoriaEnMemoria _auditor = new();
    private readonly ServicioTarjetas _servicio;

    private readonly Guid _mio = Guid.NewGuid();
    private readonly Guid _otro = Guid.NewGuid();

    public MisTarjetasPruebas() => _servicio = new ServicioTarjetas(_repo, _base, _reloj, _auditor, _secretos);

    private Tarjeta Sembrar(Guid dueno, EstadoTarjeta estado = EstadoTarjeta.Activa, DateTime? desactivadaEn = null)
    {
        var t = new Tarjeta
        {
            UrlEstable = Tarjeta.GenerarUrlEstable(), Estado = estado, PerfilPrestadorId = dueno,
            HashCodigoActivacion = "hash-con-sal", ActivadaEn = Ahora.AddDays(-40),
            DesactivadaEn = desactivadaEn,
        };
        _repo.Tarjetas.Add(t);
        return t;
    }

    private void Escaneo(Tarjeta t, MedioEscaneo medio, DateTime cuando) =>
        _repo.Escaneos.Add(new EscaneoTarjeta { TarjetaId = t.Id, Medio = medio, OcurridoEn = cuando });

    // ------------------------------------------------------------------ API-57

    [Fact]
    public async Task Desactivar_la_propia_la_deja_desactivada_con_la_hora_del_reloj_y_sin_codigo()
    {
        var t = Sembrar(_mio);

        var r = await _servicio.DesactivarAsync(_mio, t.Id);

        Assert.True(r.Exito);
        Assert.Equal(EstadoTarjeta.Desactivada, t.Estado);
        Assert.Equal(Ahora, t.DesactivadaEn);
        Assert.Null(t.HashCodigoActivacion);
        Assert.Null(t.TarjetaSucesoraId);
        Assert.Equal(1, _base.Guardados);
    }

    [Fact]
    public async Task Desactivar_una_ajena_no_la_toca_y_responde_no_encontrado()
    {
        var ajena = Sembrar(_otro);

        var r = await _servicio.DesactivarAsync(_mio, ajena.Id);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
        Assert.Equal(EstadoTarjeta.Activa, ajena.Estado);
        Assert.Null(ajena.DesactivadaEn);
        Assert.Equal(0, _base.Guardados);
    }

    /// <summary>403 y 404 responden igual: «existe pero no es tuya» ya seria informacion.</summary>
    [Fact]
    public async Task Ajena_e_inexistente_fallan_exactamente_igual()
    {
        var ajena = Sembrar(_otro);

        var contraAjena = await _servicio.DesactivarAsync(_mio, ajena.Id);
        var contraNada = await _servicio.DesactivarAsync(_mio, Guid.NewGuid());
        var statsAjena = await _servicio.EstadisticasAsync(_mio, ajena.Id);
        var statsNada = await _servicio.EstadisticasAsync(_mio, Guid.NewGuid());

        Assert.Equal(Fallas.NoEncontrado, contraNada.Codigo);
        Assert.Equal((contraNada.Exito, contraNada.Codigo, contraNada.Mensaje),
            (contraAjena.Exito, contraAjena.Codigo, contraAjena.Mensaje));
        Assert.Equal((statsNada.Exito, statsNada.Codigo, statsNada.Mensaje),
            (statsAjena.Exito, statsAjena.Codigo, statsAjena.Mensaje));
        Assert.Equal((contraNada.Codigo, contraNada.Mensaje), (statsNada.Codigo, statsNada.Mensaje));
    }

    [Fact]
    public async Task Desactivar_dos_veces_no_es_error_y_conserva_la_primera_fecha()
    {
        var antes = Ahora.AddDays(-3);
        var t = Sembrar(_mio, EstadoTarjeta.Desactivada, desactivadaEn: antes);

        var r = await _servicio.DesactivarAsync(_mio, t.Id);

        Assert.True(r.Exito);
        Assert.Equal(EstadoTarjeta.Desactivada, t.Estado);
        Assert.Equal(antes, t.DesactivadaEn);
        Assert.Equal(0, _base.Guardados);
    }

    [Fact]
    public async Task Despues_de_desactivar_el_escaneo_ya_no_abre_la_ficha_ni_cuenta()
    {
        var t = Sembrar(_mio);
        t.PerfilPrestador = new Dominio.Identidad.PerfilPrestador
        {
            Id = _mio, Slug = "ana", NombrePublico = "Ana", Estado = EstadoPerfil.Publicado,
        };
        await _servicio.DesactivarAsync(_mio, t.Id);

        var r = await _servicio.RegistrarEscaneoAsync(t.UrlEstable, MedioEscaneo.Nfc);

        Assert.Equal(DestinoTarjeta.NoValida, r.Destino);
        Assert.Empty(_repo.Escaneos);
    }

    // ------------------------------------------------------------------ API-56

    [Fact]
    public async Task Listar_trae_las_mias_activas_y_desactivadas_y_ninguna_ajena()
    {
        var activa = Sembrar(_mio);
        var muerta = Sembrar(_mio, EstadoTarjeta.Desactivada, Ahora.AddDays(-1));
        Sembrar(_otro);

        var lista = await _servicio.MisTarjetasAsync(_mio);

        Assert.Equal(new[] { activa.Id, muerta.Id }.OrderBy(x => x), lista.Select(x => x.Id).OrderBy(x => x));
        Assert.Equal("Activa", lista.Single(x => x.Id == activa.Id).Estado);
        Assert.Equal("Desactivada", lista.Single(x => x.Id == muerta.Id).Estado);
    }

    [Fact]
    public async Task Una_reemplazada_se_le_muestra_al_dueno_como_desactivada()
    {
        Sembrar(_mio, EstadoTarjeta.Reemplazada, Ahora.AddDays(-1));

        var lista = await _servicio.MisTarjetasAsync(_mio);

        Assert.Equal("Desactivada", Assert.Single(lista).Estado);
    }

    /// <summary>Nada de la activacion sale por API-56: ni hoy ni si alguien agrega un campo.</summary>
    [Fact]
    public void El_dto_no_tiene_nada_del_codigo_de_activacion()
    {
        var prohibidos = new[] { "Hash", "Codigo", "Intentos", "Bloqueada", "Sucesora", "Vence" };

        var propiedades = typeof(MiTarjetaDto).GetProperties().Select(p => p.Name).ToList();

        Assert.Equal(
            new[] { "Id", "UrlEstable", "Estado", "ActivadaEn", "DesactivadaEn" }.OrderBy(x => x),
            propiedades.OrderBy(x => x));
        Assert.DoesNotContain(propiedades, p => prohibidos.Any(x => p.Contains(x, StringComparison.OrdinalIgnoreCase)));
    }

    // ------------------------------------------------------------------ API-58

    [Fact]
    public async Task Las_estadisticas_cuentan_por_medio_y_por_periodo_con_los_bordes_exactos()
    {
        var t = Sembrar(_mio);
        // Tres NFC recientes contra un QR: con cifras simetricas, intercambiar los medios
        // pasaba inadvertido (lo mostro la mutacion M8c).
        Escaneo(t, MedioEscaneo.Nfc, Ahora);                                   // 7, 30, total
        Escaneo(t, MedioEscaneo.Nfc, Ahora.AddHours(-1));                      // 7, 30, total
        Escaneo(t, MedioEscaneo.Nfc, Ahora.AddDays(-2));                       // 7, 30, total
        Escaneo(t, MedioEscaneo.Qr, Ahora.AddDays(-7));                        // borde: 7, 30, total
        Escaneo(t, MedioEscaneo.Qr, Ahora.AddDays(-7).AddSeconds(-1));         // 30, total
        Escaneo(t, MedioEscaneo.Nfc, Ahora.AddDays(-30));                      // borde: 30, total
        Escaneo(t, MedioEscaneo.Nfc, Ahora.AddDays(-30).AddSeconds(-1));       // solo total
        Escaneo(t, MedioEscaneo.Qr, Ahora.AddDays(-200));                      // solo total

        var r = await _servicio.EstadisticasAsync(_mio, t.Id);

        Assert.True(r.Exito);
        Assert.Equal(new ConteoPorMedio(Nfc: 3, Qr: 1), r.Valor!.Ultimos7Dias);
        Assert.Equal(new ConteoPorMedio(Nfc: 4, Qr: 2), r.Valor.Ultimos30Dias);
        Assert.Equal(new ConteoPorMedio(Nfc: 5, Qr: 3), r.Valor.Total);
        Assert.Equal(8, r.Valor.Total.Total);
    }

    [Fact]
    public async Task Los_escaneos_de_otra_tarjeta_no_cuentan()
    {
        var mia = Sembrar(_mio);
        var otraMia = Sembrar(_mio);
        Escaneo(otraMia, MedioEscaneo.Nfc, Ahora);
        Escaneo(otraMia, MedioEscaneo.Qr, Ahora);

        var r = await _servicio.EstadisticasAsync(_mio, mia.Id);

        Assert.Equal(new ConteoPorMedio(0, 0), r.Valor!.Total);
    }

    [Fact]
    public async Task Las_estadisticas_de_una_ajena_responden_no_encontrado_sin_cifras()
    {
        var ajena = Sembrar(_otro);
        Escaneo(ajena, MedioEscaneo.Nfc, Ahora);

        var r = await _servicio.EstadisticasAsync(_mio, ajena.Id);

        Assert.False(r.Exito);
        Assert.Equal(Fallas.NoEncontrado, r.Codigo);
        Assert.Null(r.Valor);
    }
}
