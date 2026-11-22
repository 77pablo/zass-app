using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Identidad;

/// <summary>
/// ES-2. Que datos del registro completo (20-nov-2026) le faltan a una cuenta.
///
/// <para>
/// Las cuentas creadas antes no tienen RUT, telefono ni la casilla de mayoria de edad, y se les
/// piden al entrar (decision de Pablo, 20-nov-2026). Lo usan «Mi cuenta» (API-16), para que la
/// interfaz sepa que pedir, y el filtro de la API, que no deja seguir mientras falten.
/// </para>
///
/// <para>
/// <b>Las cuentas internas</b> (verificador, moderador, administrador) no tienen pendientes: el
/// requisito nacio en el registro publico, esas cuentas las crea un administrador a mano, y
/// bloquearlas podria dejar al equipo fuera del panel interno el dia del despliegue.
/// </para>
/// </summary>
public static class DatosDeCuenta
{
    public const string Rut = "rut";
    public const string Telefono = "telefono";
    public const string MayorDeEdad = "mayorDeEdad";

    public static string[] Pendientes(Usuario usuario)
    {
        if (usuario.Roles.Any(r => r.Rol is { EsInterno: true })) return Array.Empty<string>();

        var faltan = new List<string>(3);
        if (string.IsNullOrWhiteSpace(usuario.Rut)) faltan.Add(Rut);
        if (string.IsNullOrWhiteSpace(usuario.Telefono)) faltan.Add(Telefono);
        if (usuario.MayorDeEdadConfirmadoEn is null) faltan.Add(MayorDeEdad);
        return faltan.ToArray();
    }
}
