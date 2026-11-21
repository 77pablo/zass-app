using Zass.Aplicacion.Comun;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Desde el 20-nov-2026 el registro (API-08) exige tipo de cuenta, RUT, telefono y la casilla
/// de mayor de 18. El RUT es unico en la base y la base de pruebas NO se vacia entre corridas,
/// asi que cada registro lleva uno valido y al azar.
/// </summary>
public static class DatosDeRegistro
{
    /// <summary>
    /// Un RUT valido y distinto en cada llamada: el cuerpo sale al azar entre 10 y 99 millones
    /// (90 millones de posibilidades) y el digito se calcula con modulo 11.
    /// </summary>
    public static string Rut()
    {
        var cuerpo = Random.Shared.Next(10_000_000, 99_999_999);
        return $"{cuerpo}-{Zass.Aplicacion.Comun.Rut.DigitoVerificador(cuerpo)}";
    }
}
