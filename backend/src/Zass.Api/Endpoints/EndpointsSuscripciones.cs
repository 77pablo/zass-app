using System.Security.Claims;
using System.Text.Json;
using FluentValidation;
using Zass.Api.Configuracion;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Aplicacion.Suscripciones;

namespace Zass.Api.Endpoints;

/// <summary>API-67. Lo que se cobraria, antes de que exista cobro.</summary>
public sealed record CotizarHttpCmd(string CodigoPlan, string? CodigoCupon);

/// <summary>API-68. Empieza el cobro de un mes.</summary>
public sealed record IniciarCobroHttpCmd(string CodigoPlan, string? CodigoCupon);

// Aqui solo se acota la FORMA. Las reglas de verdad —que el plan exista, que el cupon
// sirva, que queden cupos, cuanto se cobra— las aplica MOD-13, y por eso no se repiten.
public sealed class ValidadorCotizar : AbstractValidator<CotizarHttpCmd>
{
    public ValidadorCotizar()
    {
        RuleFor(x => x.CodigoPlan).NotEmpty().MaximumLength(30)
            .WithMessage("Falta el plan.");
        RuleFor(x => x.CodigoCupon).MaximumLength(30)
            .WithMessage("Ese código de descuento es demasiado largo.");
    }
}

public sealed class ValidadorIniciarCobro : AbstractValidator<IniciarCobroHttpCmd>
{
    public ValidadorIniciarCobro()
    {
        RuleFor(x => x.CodigoPlan).NotEmpty().MaximumLength(30)
            .WithMessage("Falta el plan.");
        RuleFor(x => x.CodigoCupon).MaximumLength(30)
            .WithMessage("Ese código de descuento es demasiado largo.");
    }
}

/// <summary>
/// API-66 a API-70. S7 · Suscripciones y Pagos.
///
/// <list type="bullet">
/// <item><c>GET /api/planes</c> — publico: el catalogo y cuantos cupos fundador quedan</item>
/// <item><c>/api/mi/cobros...</c> — del prestador. El perfil sale de la SESION, nunca del
/// cuerpo: con un id en el cuerpo, cualquiera pagaria por otro o le tomaria el cupo</item>
/// <item><c>/api/pagos/aviso</c> — lo llama un SERVIDOR, no un navegador</item>
/// </list>
///
/// <para>
/// Las dos excepciones de API-70, dichas aqui porque son excepciones de verdad a la regla 8
/// de CLAUDE.md: <b>no lleva sesion y no lleva CSRF</b>. No hay navegador ni cookie que
/// proteger; quien verifica que el aviso viene de quien dice venir es el adaptador de la
/// pasarela (firma, consulta de confirmacion o lista de IP, segun la pasarela), y es el
/// unico que sabe como. Esta clase no interpreta ni un campo del aviso: lo recoge tal como
/// llego y lo entrega.
/// </para>
///
/// <para>
/// Y un detalle que no es un detalle: un aviso repetido responde <b>200</b>. Una pasarela
/// que recibe un error reintenta, y lo hace a las tres de la manana. La idempotencia la
/// garantiza el intento (TBL-36) con su bloqueo de fila, no esta capa.
/// </para>
/// </summary>
public static class EndpointsSuscripciones
{
    public static IEndpointRouteBuilder MapearSuscripciones(this IEndpointRouteBuilder app)
    {
        MapearPublico(app.MapGroup("/api"));
        MapearMisCobros(app.MapGroup("/api/mi"));
        MapearAvisos(app.MapGroup("/api/pagos"));
        return app;
    }

    private static void MapearPublico(RouteGroupBuilder grupo)
    {
        // API-66 - Los planes activos y cuantos cupos fundador quedan.
        //
        // Sin un solo dato de ninguna persona, a proposito: es publico, lo pide la pagina
        // de planes antes de que nadie entre, y el contador es justamente el gancho del
        // lanzamiento («quedan N de 50»).
        grupo.MapGet("/planes", async (IServicioSuscripciones suscripciones) =>
                Results.Ok(new
                {
                    planes = await suscripciones.PlanesActivosAsync(),
                    cuposFundadorDisponibles = await suscripciones.CuposFundadorDisponiblesAsync(),
                }))
            .RequireRateLimiting(Tasa.Publico)
            .WithName("API-66");
    }

    private static void MapearMisCobros(RouteGroupBuilder grupo)
    {
        grupo.RequireAuthorization();
        grupo.AddEndpointFilter<FiltroCsrf>();

        // API-67 - Cuanto se cobraria por ese plan con ese cupon. NO escribe nada.
        //
        // Existe porque la pantalla tiene que poder mostrar «pagas $5.593 con tu código»
        // antes de que exista un cobro, y porque un total sin el precio de lista y el
        // descuento al lado no se puede comprobar.
        grupo.MapPost("/cobros/cotizar", async (
                CotizarHttpCmd cmd, IServicioCobros cobros,
                ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                var mio = await MiPerfil(perfiles, quien);
                if (!mio.Exito) return Respuestas.Problema(mio);

                return Respuestas.DeResultado(await cobros.CotizarAsync(
                    mio.Valor!.Id, cmd.CodigoPlan, cmd.CodigoCupon));
            })
            .ConValidacion<CotizarHttpCmd>()
            .RequireRateLimiting(Tasa.General)
            .WithName("API-67");

        // API-68 - Empieza el cobro de un mes y devuelve a donde mandar a la persona.
        //
        // El filtro de idempotencia va ANTES del endpoint: sin la cabecera no se ejecuta
        // nada. La clave la genera el cliente al abrir el formulario y la repite en cada
        // reintento del MISMO envio (CC-04); una clave que pusiera el servidor no
        // protegeria de nada, porque cada reintento traeria otra.
        grupo.MapPost("/cobros", async (
                IniciarCobroHttpCmd cmd, HttpContext http, IServicioCobros cobros,
                ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                if (http.ClaveIdempotencia() is not { } clave) return FaltaLaClave();

                var mio = await MiPerfil(perfiles, quien);
                if (!mio.Exito) return Respuestas.Problema(mio);

                return Respuestas.DeResultado(await cobros.IniciarAsync(
                    new IniciarCobroCmd(mio.Valor!.Id, cmd.CodigoPlan, cmd.CodigoCupon, clave)));
            })
            .ConValidacion<IniciarCobroHttpCmd>()
            .AddEndpointFilter<FiltroIdempotencia>()
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-68");

        // API-69 - Toma un cupo fundador inscribiendo el medio de pago (RN-S7-01).
        //
        // Sin cuerpo a proposito: no hay nada que elegir. El plan es el fundador, el monto
        // es cero y el perfil sale de la sesion. Lo unico que viaja es la clave de
        // idempotencia, para que el doble clic no tome dos cupos.
        grupo.MapPost("/suscripcion/fundador", async (
                HttpContext http, IServicioCobros cobros,
                ServicioPerfiles perfiles, ClaimsPrincipal quien) =>
            {
                if (http.ClaveIdempotencia() is not { } clave) return FaltaLaClave();

                var mio = await MiPerfil(perfiles, quien);
                if (!mio.Exito) return Respuestas.Problema(mio);

                return Respuestas.DeResultado(
                    await cobros.IniciarInscripcionFundadorAsync(mio.Valor!.Id, clave));
            })
            .AddEndpointFilter<FiltroIdempotencia>()
            .RequireRateLimiting(Tasa.Sensible)
            .WithName("API-69");
    }

    private static void MapearAvisos(RouteGroupBuilder grupo)
    {
        // API-70 - El aviso de la pasarela. Sin sesion y sin CSRF (ver el comentario de la
        // clase). Grupo G y no S: si la pasarela reintenta, no queremos que el limitador
        // sea el que decida que un pago no se registre.
        grupo.MapPost("/aviso", async (HttpRequest peticion, IServicioCobros cobros) =>
            {
                var campos = await CamposDelAviso(peticion);
                if (campos is null)
                    return Results.Problem(
                        title: "Aviso ilegible",
                        detail: "No se pudo leer el contenido del aviso.",
                        statusCode: StatusCodes.Status400BadRequest);

                var r = await cobros.ConfirmarAsync(new AvisoDePasarela(campos));

                // Exito incluye el duplicado y el pago rechazado: los dos son desenlaces
                // normales y la pasarela no tiene que reintentar ninguno.
                return r.Exito ? Results.Ok(r.Valor) : Respuestas.Problema(r);
            })
            .RequireRateLimiting(Tasa.General)
            .WithName("API-70");
    }

    /// <summary>
    /// El perfil de prestador de quien llama. Sale de la sesion y de ningun otro sitio.
    /// </summary>
    private static async Task<Resultado<MiPerfilPrestadorDto>> MiPerfil(
        ServicioPerfiles perfiles, ClaimsPrincipal quien)
    {
        var usuarioId = quien.UsuarioId();
        if (usuarioId is null)
            return Resultado.Falla<MiPerfilPrestadorDto>(
                Fallas.NoAutorizado, "No hay sesión.");

        return await perfiles.MiPerfilPrestadorAsync(usuarioId.Value);
    }

    /// <summary>
    /// Los campos del aviso, de la cadena de consulta y del cuerpo, sin interpretar
    /// ninguno. Nulo si el cuerpo no se pudo leer.
    ///
    /// Se aceptan los tres formatos que usan las pasarelas chilenas —cadena de consulta,
    /// formulario y JSON— porque elegir uno aqui seria elegir la pasarela en el endpoint,
    /// que es justo lo que el puerto existe para evitar.
    /// </summary>
    private static async Task<Dictionary<string, string>?> CamposDelAviso(HttpRequest peticion)
    {
        var campos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (clave, valor) in peticion.Query)
            if (valor.Count > 0 && valor[0] is { } v) campos[clave] = v;

        try
        {
            if (peticion.HasFormContentType)
            {
                var formulario = await peticion.ReadFormAsync();
                foreach (var (clave, valor) in formulario)
                    if (valor.Count > 0 && valor[0] is { } v) campos[clave] = v;
            }
            else if (peticion.ContentLength is > 0)
            {
                using var documento = await JsonDocument.ParseAsync(peticion.Body);
                if (documento.RootElement.ValueKind != JsonValueKind.Object) return campos;

                foreach (var propiedad in documento.RootElement.EnumerateObject())
                    campos[propiedad.Name] = propiedad.Value.ValueKind == JsonValueKind.String
                        ? propiedad.Value.GetString() ?? ""
                        : propiedad.Value.ToString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return campos;
    }

    private static IResult FaltaLaClave() => Results.Problem(
        title: "Falta la clave de idempotencia",
        detail: $"Manda la cabecera {FiltroIdempotencia.Cabecera} con cada intento del mismo envío.",
        statusCode: StatusCodes.Status400BadRequest);
}
