using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Tarjetas;

namespace Zass.Aplicacion.Pruebas.Tarjetas;

/// <summary>
/// CL-24 · <see cref="Tarjeta.Resolver"/>: qué ve quien escanea una tarjeta, según su ciclo
/// (S6 §4). El caso que más importa es RN-S6-06: una tarjeta desactivada o reemplazada sigue
/// teniendo un dueño publicado en la base, y aun así no puede llevar a su ficha.
/// </summary>
public class TarjetaResolverPruebas
{
    private static PerfilPrestador Perfil(EstadoPerfil estado) =>
        new() { Slug = "ana-gasfiter", NombrePublico = "Ana", Estado = estado };

    private static Tarjeta Tarjeta(EstadoTarjeta estado, PerfilPrestador? perfil) => new()
    {
        UrlEstable = "7KQ2-9XRM",
        Estado = estado,
        PerfilPrestador = perfil,
        PerfilPrestadorId = perfil?.Id,
    };

    [Fact]
    public void Una_tarjeta_emitida_sin_dueno_lleva_a_la_activacion()
    {
        Assert.Equal(DestinoTarjeta.Activacion,
            Tarjeta(EstadoTarjeta.Emitida, null).Resolver());
    }

    /// <summary>
    /// Mientras esté emitida manda la activación, aunque por error ya tuviera un perfil
    /// publicado colgando: no hay ficha sin activar antes.
    /// </summary>
    [Fact]
    public void Una_tarjeta_emitida_no_abre_la_ficha_aunque_tenga_perfil_publicado()
    {
        Assert.Equal(DestinoTarjeta.Activacion,
            Tarjeta(EstadoTarjeta.Emitida, Perfil(EstadoPerfil.Publicado)).Resolver());
    }

    [Fact]
    public void Una_tarjeta_activa_con_perfil_publicado_abre_la_ficha()
    {
        Assert.Equal(DestinoTarjeta.Ficha,
            Tarjeta(EstadoTarjeta.Activa, Perfil(EstadoPerfil.Publicado)).Resolver());
    }

    /// <summary>RN-S6-09: perfil no publicado → «perfil no disponible», no un error.</summary>
    [Theory]
    [InlineData(EstadoPerfil.Borrador)]
    [InlineData(EstadoPerfil.Despublicado)]
    public void Una_tarjeta_activa_con_perfil_no_publicado_muestra_perfil_no_disponible(EstadoPerfil estado)
    {
        Assert.Equal(DestinoTarjeta.PerfilNoDisponible,
            Tarjeta(EstadoTarjeta.Activa, Perfil(estado)).Resolver());
    }

    /// <summary>
    /// Sin el perfil cargado (o sin perfil) no se puede afirmar que esté publicado: se cae
    /// del lado seguro, nunca a la ficha.
    /// </summary>
    [Fact]
    public void Una_tarjeta_activa_sin_perfil_cargado_muestra_perfil_no_disponible()
    {
        Assert.Equal(DestinoTarjeta.PerfilNoDisponible,
            Tarjeta(EstadoTarjeta.Activa, null).Resolver());
    }

    /// <summary>RN-S6-06: aunque el dueño siga publicado, la tarjeta muerta no lo revela.</summary>
    [Fact]
    public void Una_tarjeta_desactivada_no_revela_a_su_dueno_publicado()
    {
        Assert.Equal(DestinoTarjeta.NoValida,
            Tarjeta(EstadoTarjeta.Desactivada, Perfil(EstadoPerfil.Publicado)).Resolver());
    }

    [Fact]
    public void Una_tarjeta_reemplazada_no_revela_a_su_dueno_publicado()
    {
        var vieja = Tarjeta(EstadoTarjeta.Reemplazada, Perfil(EstadoPerfil.Publicado));
        vieja.TarjetaSucesoraId = Guid.NewGuid();

        Assert.Equal(DestinoTarjeta.NoValida, vieja.Resolver());
    }

    /// <summary>
    /// Desactivar es inmediato (RN-S6-05): tras <see cref="Tarjeta.Desactivar"/> la misma
    /// tarjeta, con el mismo dueño publicado, ya no abre la ficha, y el código no queda vivo.
    /// </summary>
    [Fact]
    public void Desactivar_una_tarjeta_activa_la_vuelve_no_valida_y_borra_el_codigo()
    {
        var tarjeta = Tarjeta(EstadoTarjeta.Activa, Perfil(EstadoPerfil.Publicado));
        tarjeta.HashCodigoActivacion = "hash-con-sal";
        Assert.Equal(DestinoTarjeta.Ficha, tarjeta.Resolver());

        var antes = DateTime.UtcNow;
        tarjeta.Desactivar();

        Assert.Equal(EstadoTarjeta.Desactivada, tarjeta.Estado);
        Assert.NotNull(tarjeta.DesactivadaEn);
        Assert.InRange(tarjeta.DesactivadaEn!.Value, antes, DateTime.UtcNow);
        Assert.Null(tarjeta.HashCodigoActivacion);
        Assert.Equal(DestinoTarjeta.NoValida, tarjeta.Resolver());
    }
}
