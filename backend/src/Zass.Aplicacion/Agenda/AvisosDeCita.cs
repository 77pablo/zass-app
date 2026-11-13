namespace Zass.Aplicacion.Agenda;

/// <summary>
/// La carga de los avisos de cita (JQ-1). Un mismo tipo puede ir a las dos partes de la cita
/// —una cancelacion le llega a quien NO cancelo, sea prestador o buscador—, asi que la carga
/// dice a quien va, y la plantilla elige el texto y el enlace segun eso.
///
/// Solo el rol: nada de la cita (ni fecha, ni hora, ni servicio) y nada de la otra persona.
/// Es la regla de <c>Plantillas</c>: el correo lleva el nombre del propio destinatario y un
/// enlace, y lo demas se ve dentro de ZASS!.
/// </summary>
public static class AvisosDeCita
{
    public const string CampoRol = "rol";
    public const string RolPrestador = "prestador";
    public const string RolBuscador = "buscador";
}
