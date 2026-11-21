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

    /// <summary>E-413 de CU-05: el archivo pasa el tope. El mensaje dice el tamaño real y el límite.</summary>
    public const string ArchivoDemasiadoGrande = "archivo_demasiado_grande";

    /// <summary>E-415 de CU-05: el contenido no es de un tipo aceptado (se mira por dentro, no el nombre).</summary>
    public const string TipoDeArchivoNoAceptado = "tipo_de_archivo_no_aceptado";

    /// <summary>
    /// E-507 de CU-05: el almacenamiento no guardó, o todavía no está configurado. No es culpa de
    /// quien sube y puede reintentar; por eso no es un 4xx.
    /// </summary>
    public const string AlmacenamientoNoDisponible = "almacenamiento_no_disponible";

    /// <summary>
    /// API-08 con un RUT que ya tiene cuenta (decision de Pablo, 21-nov-2026). Es un 409 como
    /// cualquier conflicto, pero lleva su propio codigo para que la pantalla lo muestre como
    /// aviso y no como error: la cuenta no se crea y a la duena se le manda un correo.
    /// </summary>
    public const string RutConCuenta = "rut_con_cuenta";
}
