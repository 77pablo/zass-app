namespace Zass.Infraestructura.Correo;

/// <summary>Los registros de seguridad no llevan direcciones completas (seguridad.md).</summary>
public static class Enmascarar
{
    /// <summary>abel@dominio.cl pasa a a***@dominio.cl. Lo que no parece un correo se oculta entero.</summary>
    public static string Correo(string? correo)
    {
        if (string.IsNullOrWhiteSpace(correo)) return "***";
        var arroba = correo.LastIndexOf('@');
        if (arroba < 1 || arroba == correo.Length - 1) return "***";
        return $"{correo[0]}***{correo[arroba..]}";
    }
}
