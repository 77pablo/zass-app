using Zass.Aplicacion.Busqueda;
using Zass.Aplicacion.Catalogo;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Geografia;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Busqueda;

/// <summary>
/// RN-S6-10 en la ficha publica (CU-09 · API-05): el prestador que dejo de pagar SIGUE
/// teniendose ficha, pero sin manera de contactarlo ni de agendarle.
///
/// Es la regla que mas facil se implementa mal: esconder los botones en la pantalla y dejar
/// el telefono viajando en la respuesta. Por eso aqui se comprueba lo que llega del servidor,
/// no lo que se pinta.
///
/// Archivo aparte de <see cref="FichaPublicaPruebas"/> a proposito: aquella prueba la
/// COMPOSICION de la ficha; esta, una sola regla de negocio sobre ella.
/// </summary>
public class FichaSinSuscripcionPruebas
{
    private const short Corretaje = 1;

    private readonly BaseEnMemoria _base = new();
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc));
    private readonly ServicioBusqueda _servicio;

    public FichaSinSuscripcionPruebas()
    {
        _base.Rubros.Add(new Rubro
        {
            Id = Corretaje, Codigo = "corretaje", Nombre = "Corretaje de propiedades", Activo = true,
        });

        var auditor = new AuditoriaEnMemoria();
        _servicio = new ServicioBusqueda(
            new RepositorioBusquedaFalso(),
            new ServicioCatalogo(new RepositorioCatalogoEnMemoria(_base), _base, _reloj, auditor),
            new ServicioGeografia(
                new RepositorioGeografiaEnMemoria(_base), new GeocodificadorApagado(), _base, _reloj, auditor),
            new ServicioIdentidad(
                new RepositorioUsuariosEnMemoria(_base), new RepositorioPerfilesEnMemoria(_base)),
            new ServicioCredenciales(new RepositorioCredencialesEnMemoria(_base), _base, _reloj, auditor),
            // MOD-13 de verdad sobre la misma lista. Un doble que respondiera «si» siempre
            // borraria justo la regla que esta clase prueba.
            new ServicioSuscripciones(
                new RepositorioSuscripcionesEnMemoria(_base), _base, _reloj, auditor, new ColaEnMemoria()),
            _reloj);
    }

    /// <summary>Publicado y con los cuatro canales de contacto llenos: hay algo que apagar.</summary>
    private PerfilPrestador UnPerfil(EstadoSuscripcion? suscripcion, int diasDeVigencia = 25)
    {
        var perfil = new PerfilPrestador
        {
            UsuarioId = Guid.NewGuid(),
            Slug = "marcela-soto",
            NombrePublico = "Marcela Soto",
            Descripcion = "Corredora de propiedades en Temuco.",
            RubroId = Corretaje,
            Estado = EstadoPerfil.Publicado,
            PublicadoEn = _reloj.Ahora.AddDays(-30),
            TelefonoPublico = "+56 9 1234 5678",
            Whatsapp = "912345678",
            CorreoPublico = "contacto@soto.cl",
            SitioWeb = "https://soto.cl",
        };
        _base.PerfilesPrestador.Add(perfil);

        if (suscripcion is not null)
        {
            _base.Suscripciones.Add(new Suscripcion
            {
                PerfilPrestadorId = perfil.Id, PlanId = 1, Estado = suscripcion.Value,
                IniciaEn = _reloj.Ahora.AddDays(-60),
                VigenteHasta = _reloj.Ahora.AddDays(diasDeVigencia),
            });
        }

        _base.Servicios.Add(new Servicio
        {
            Id = 101, RubroId = Corretaje, Nombre = "Tasación comercial",
            NombreNormalizado = "tasacion comercial", EsCatalogo = true,
        });
        _base.PrestadorServicios.Add(new PrestadorServicio
        {
            PerfilPrestadorId = perfil.Id, ServicioId = 101, DuracionMinutos = 60, Activo = true,
        });

        return perfil;
    }

    [Fact]
    public async Task Con_la_suscripcion_vigente_la_ficha_trae_el_contacto_y_lo_dice()
    {
        UnPerfil(EstadoSuscripcion.Activa);

        var ficha = await _servicio.FichaPublicaAsync("marcela-soto");

        Assert.True(ficha!.SuscripcionVigente);
        Assert.Equal("+56 9 1234 5678", ficha.Contacto.Telefono);
        Assert.Equal("912345678", ficha.Contacto.Whatsapp);
        Assert.Equal("contacto@soto.cl", ficha.Contacto.Correo);
        Assert.Equal("https://soto.cl", ficha.Contacto.SitioWeb);
    }

    /// <summary>
    /// El corazon de RN-S6-10: la ficha NO desaparece. Lo que desaparece es el contacto.
    /// </summary>
    [Theory]
    [InlineData(null, 25)]                              // nunca tuvo suscripcion
    [InlineData(EstadoSuscripcion.Vencida, 25)]         // marcada vencida por el trabajo de fondo
    [InlineData(EstadoSuscripcion.Cancelada, 25)]
    [InlineData(EstadoSuscripcion.Activa, -1)]          // activa en papel, pero pasada de fecha
    public async Task Sin_suscripcion_vigente_la_ficha_se_ve_entera_pero_sin_contacto(
        EstadoSuscripcion? estado, int diasDeVigencia)
    {
        UnPerfil(estado, diasDeVigencia);

        var ficha = await _servicio.FichaPublicaAsync("marcela-soto");

        Assert.NotNull(ficha);
        Assert.False(ficha!.SuscripcionVigente);

        // Lo que SIGUE: la ficha es visible. Apagarla del todo dejaria a quien tiene la
        // tarjeta en la mano sin entender que paso.
        Assert.Equal("Marcela Soto", ficha.NombrePublico);
        Assert.Equal("Corredora de propiedades en Temuco.", ficha.Descripcion);
        Assert.Equal("Corretaje de propiedades", ficha.Rubro!.Nombre);
        Assert.Single(ficha.Servicios);

        // Lo que se apaga, y se apaga EN EL SERVIDOR: ni telefono, ni WhatsApp, ni correo,
        // ni sitio. Esconder los botones no bastaria: el dato seguiria en la respuesta.
        Assert.Null(ficha.Contacto.Telefono);
        Assert.Null(ficha.Contacto.Whatsapp);
        Assert.Null(ficha.Contacto.Correo);
        Assert.Null(ficha.Contacto.SitioWeb);
    }

    /// <summary>
    /// RN-S6-09 sigue siendo otra cosa: el perfil despublicado por su dueño no tiene ficha,
    /// con o sin suscripcion. Si esto se pusiera rojo, es que RN-S6-10 se comio a RN-S6-09.
    /// </summary>
    [Fact]
    public async Task Un_perfil_despublicado_sigue_sin_ficha_aunque_la_suscripcion_este_al_dia()
    {
        var perfil = UnPerfil(EstadoSuscripcion.Activa);
        perfil.Estado = EstadoPerfil.Despublicado;

        Assert.Null(await _servicio.FichaPublicaAsync("marcela-soto"));
    }
}
