using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Tarjetas;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Tarjetas;

namespace Zass.Aplicacion.Pruebas.Tarjetas;

/// <summary>
/// CS-18 · Registrar escaneo (incluye CU-09). Lo que se cuenta, lo que no, y que una tarjeta
/// muerta no se distinga de una que nunca existio (RN-S6-06).
/// </summary>
public class RegistrarEscaneoPruebas
{
    private const string Segmento = "7KQ2-9XRM";

    private readonly RepositorioTarjetasEnMemoria _repo = new();
    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 12, 15, 30, 0, DateTimeKind.Utc));
    private readonly ServicioTarjetas _servicio;

    public RegistrarEscaneoPruebas() => _servicio = new ServicioTarjetas(_repo, _base, _reloj);

    private Tarjeta Sembrar(EstadoTarjeta estado, EstadoPerfil? perfil = EstadoPerfil.Publicado)
    {
        var p = perfil is null ? null
            : new PerfilPrestador { Slug = "ana-gasfiter", NombrePublico = "Ana", Estado = perfil.Value };
        var t = new Tarjeta { UrlEstable = Segmento, Estado = estado, PerfilPrestador = p, PerfilPrestadorId = p?.Id };
        _repo.Tarjetas.Add(t);
        return t;
    }

    [Fact]
    public async Task Una_tarjeta_activa_abre_la_ficha_y_cuenta_un_escaneo_sin_identidad()
    {
        var tarjeta = Sembrar(EstadoTarjeta.Activa);

        var r = await _servicio.RegistrarEscaneoAsync(Segmento, MedioEscaneo.Qr);

        Assert.Equal(new EscaneoResuelto(DestinoTarjeta.Ficha, "ana-gasfiter"), r);
        var escaneo = Assert.Single(_repo.Escaneos);
        Assert.Equal(tarjeta.Id, escaneo.TarjetaId);
        Assert.Equal(MedioEscaneo.Qr, escaneo.Medio);
        Assert.Equal(_reloj.Ahora, escaneo.OcurridoEn);
        Assert.False(escaneo.DerivoEnContacto);
        Assert.Equal(1, _base.Guardados);
    }

    [Theory]
    [InlineData(EstadoTarjeta.Desactivada)]
    [InlineData(EstadoTarjeta.Reemplazada)]
    public async Task Una_tarjeta_muerta_no_revela_al_dueno_ni_cuenta(EstadoTarjeta estado)
    {
        Sembrar(estado);

        var r = await _servicio.RegistrarEscaneoAsync(Segmento, MedioEscaneo.Nfc);

        Assert.Equal(new EscaneoResuelto(DestinoTarjeta.NoValida, null), r);
        Assert.Empty(_repo.Escaneos);
        Assert.Equal(0, _base.Guardados);
    }

    /// <summary>RN-S6-06: desde afuera, «desactivada» y «nunca existio» son la misma respuesta.</summary>
    [Fact]
    public async Task Una_tarjeta_inexistente_responde_igual_que_una_desactivada()
    {
        var inexistente = await _servicio.RegistrarEscaneoAsync(Segmento, MedioEscaneo.Nfc);
        Sembrar(EstadoTarjeta.Desactivada);
        var desactivada = await _servicio.RegistrarEscaneoAsync(Segmento, MedioEscaneo.Nfc);

        Assert.Equal(desactivada, inexistente);
        Assert.Equal(2, _repo.Consultas);   // las dos fueron a la base: mismo trabajo
        Assert.Empty(_repo.Escaneos);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("7KQ2")]
    [InlineData("7KQ29XRM")]
    [InlineData("7KQ2-9XRM-AAAA")]
    [InlineData("7KQ2-9XR0")]      // el 0 no esta en el alfabeto
    [InlineData("7KQ2-9XRI")]      // la I tampoco
    [InlineData("../x")]
    public async Task Un_segmento_malformado_no_llega_a_la_base(string? segmento)
    {
        Sembrar(EstadoTarjeta.Activa);

        var r = await _servicio.RegistrarEscaneoAsync(segmento, MedioEscaneo.Nfc);

        Assert.Equal(new EscaneoResuelto(DestinoTarjeta.NoValida, null), r);
        Assert.Equal(0, _repo.Consultas);
        Assert.Empty(_repo.Escaneos);
    }

    [Fact]
    public async Task Un_segmento_en_minusculas_y_con_espacios_resuelve_igual()
    {
        Sembrar(EstadoTarjeta.Activa);

        var r = await _servicio.RegistrarEscaneoAsync(" 7kq2-9xrm ", MedioEscaneo.Nfc);

        Assert.Equal(DestinoTarjeta.Ficha, r.Destino);
        Assert.Single(_repo.Escaneos);
    }

    [Fact]
    public async Task Una_tarjeta_emitida_lleva_a_la_activacion_sin_contar()
    {
        Sembrar(EstadoTarjeta.Emitida, perfil: null);

        var r = await _servicio.RegistrarEscaneoAsync(Segmento, MedioEscaneo.Nfc);

        Assert.Equal(new EscaneoResuelto(DestinoTarjeta.Activacion, null), r);
        Assert.Empty(_repo.Escaneos);
        Assert.Equal(0, _base.Guardados);
    }

    /// <summary>RN-S6-09: perfil despublicado → «perfil no disponible», sin slug y sin contar.</summary>
    [Fact]
    public async Task Una_tarjeta_activa_con_perfil_despublicado_no_entrega_el_slug_ni_cuenta()
    {
        Sembrar(EstadoTarjeta.Activa, EstadoPerfil.Despublicado);

        var r = await _servicio.RegistrarEscaneoAsync(Segmento, MedioEscaneo.Nfc);

        Assert.Equal(new EscaneoResuelto(DestinoTarjeta.PerfilNoDisponible, null), r);
        Assert.Empty(_repo.Escaneos);
    }

    [Fact]
    public async Task Un_medio_fuera_de_rango_se_guarda_como_Nfc()
    {
        Sembrar(EstadoTarjeta.Activa);

        await _servicio.RegistrarEscaneoAsync(Segmento, (MedioEscaneo)99);

        Assert.Equal(MedioEscaneo.Nfc, Assert.Single(_repo.Escaneos).Medio);
    }

    [Fact]
    public async Task Un_escaneo_por_NFC_se_guarda_como_Nfc()
    {
        Sembrar(EstadoTarjeta.Activa);

        await _servicio.RegistrarEscaneoAsync(Segmento, MedioEscaneo.Nfc);

        Assert.Equal(MedioEscaneo.Nfc, Assert.Single(_repo.Escaneos).Medio);
    }
}
