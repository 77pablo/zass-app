namespace Zass.Api.Configuracion;

/// <summary>
/// CC-04. Los endpoints de api-rest.md marcados con **I** exigen la cabecera
/// `Idempotency-Key`: API-36 crear cita, API-40 cancelar, API-41 reprogramar y API-48
/// publicar reseña.
///
/// <para>
/// **Sin la cabecera responden 400.** Lo dice el documento y la razón es exacta: «no se asume
/// una clave por defecto: una clave generada por el servidor no protege de nada». Una clave
/// nueva por solicitud haría que el doble envío pareciera dos intenciones distintas, que es
/// justo lo que la clave existe para distinguir.
/// </para>
///
/// Este filtro solo **valida y entrega** la clave. Quién decide qué hacer con una repetida es
/// el caso de uso: en API-36 devuelve la misma cita (flujo 4B de CU-12), y la garantía última
/// es el índice único sobre la columna, no este filtro.
/// </summary>
public sealed class FiltroIdempotencia : IEndpointFilter
{
    public const string Cabecera = "Idempotency-Key";

    /// <summary>
    /// Donde queda la clave para el endpoint. Se pasa por <c>HttpContext.Items</c> y no como
    /// parámetro para que el filtro pueda aplicarse a un grupo entero.
    /// </summary>
    public const string Clave = "Zass.ClaveIdempotencia";

    /// <summary>
    /// Un UUID son 36 caracteres; se aceptan hasta 120 por si el cliente prefiere otro
    /// formato. El tope existe porque la clave se guarda y se indexa: sin él, una cabecera
    /// de 8 KB entraría a la base.
    /// </summary>
    public const int LargoMaximo = 120;

    public const int LargoMinimo = 8;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente)
    {
        var valores = contexto.HttpContext.Request.Headers[Cabecera];

        // Dos cabeceras distintas no es «una intención»: es una solicitud ambigua, y elegir
        // una de las dos en silencio sería inventar cuál era la intención.
        if (valores.Count > 1)
            return Problema("Llegaron dos valores de Idempotency-Key en la misma solicitud.");

        var clave = valores.Count == 1 ? valores[0]?.Trim() : null;
        if (string.IsNullOrEmpty(clave))
            return Problema(
                $"Falta la cabecera {Cabecera}. Genera un identificador único al abrir el "
                + "formulario y mándalo con cada intento del MISMO envío.");

        if (clave.Length is < LargoMinimo or > LargoMaximo)
            return Problema(
                $"La cabecera {Cabecera} tiene que medir entre {LargoMinimo} y {LargoMaximo} caracteres.");

        // Se restringe el alfabeto: la clave viaja a un índice y a los registros, y aceptar
        // cualquier byte invita a meter ahí texto que no es un identificador.
        foreach (var c in clave)
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or ':'))
                return Problema(
                    $"La cabecera {Cabecera} solo admite letras, dígitos, guion, guion bajo y dos puntos.");

        contexto.HttpContext.Items[Clave] = clave;
        return await siguiente(contexto);
    }

    private static IResult Problema(string detalle) => Results.Problem(
        title: "Falta la clave de idempotencia",
        detail: detalle,
        statusCode: StatusCodes.Status400BadRequest);
}

public static class Idempotencia
{
    /// <summary>
    /// La clave que dejó <see cref="FiltroIdempotencia"/>. Si el filtro no corrió, no hay
    /// clave: devuelve null y el caso de uso falla con un mensaje claro en vez de agendar
    /// sin protección.
    /// </summary>
    public static string? ClaveIdempotencia(this HttpContext http) =>
        http.Items.TryGetValue(FiltroIdempotencia.Clave, out var v) ? v as string : null;
}
