namespace Zass.Dominio.Comun;

/// <summary>
/// Resultado de una operacion de dominio. Se usa en vez de excepciones para los casos
/// esperados: un bloque ya reservado o una resena sin elegibilidad no son errores del
/// programa, son desenlaces normales que quien llama tiene que tratar.
/// </summary>
public class Resultado
{
    public bool Exito { get; }
    public string? Codigo { get; }
    public string? Mensaje { get; }

    protected Resultado(bool exito, string? codigo, string? mensaje)
    {
        Exito = exito;
        Codigo = codigo;
        Mensaje = mensaje;
    }

    public static Resultado Ok() => new(true, null, null);

    /// <summary>
    /// Exito que ademas lleva un texto para la persona. Lo necesita API-14: responde lo
    /// mismo exista o no la cuenta, y ese texto es justamente la respuesta.
    /// </summary>
    public static Resultado OkConMensaje(string mensaje) => new(true, null, mensaje);
    public static Resultado Falla(string codigo, string mensaje) => new(false, codigo, mensaje);

    public static Resultado<T> Ok<T>(T valor) => Resultado<T>.Correcto(valor);
    public static Resultado<T> Falla<T>(string codigo, string mensaje) =>
        Resultado<T>.Incorrecto(codigo, mensaje);
}

public sealed class Resultado<T> : Resultado
{
    public T? Valor { get; }

    private Resultado(bool exito, T? valor, string? codigo, string? mensaje)
        : base(exito, codigo, mensaje) => Valor = valor;

    internal static Resultado<T> Correcto(T valor) => new(true, valor, null, null);
    internal static Resultado<T> Incorrecto(string codigo, string mensaje) =>
        new(false, default, codigo, mensaje);
}

/// <summary>Codigos de falla que la capa de API traduce a un estado HTTP.</summary>
public static class Fallas
{
    public const string NoAutorizado = "no_autorizado";
    public const string NoEncontrado = "no_encontrado";
    public const string Conflicto = "conflicto";
    public const string ReglaDeNegocio = "regla_de_negocio";
    public const string Validacion = "validacion";
    public const string VersionDesactualizada = "version_desactualizada";
}
