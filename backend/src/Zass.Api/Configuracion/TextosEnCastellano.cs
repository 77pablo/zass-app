using System.Globalization;
using FluentValidation;
using Microsoft.AspNetCore.WebUtilities;

namespace Zass.Api.Configuracion;

/// <summary>
/// Lo que ASP.NET y FluentValidation dicen por su cuenta, en castellano.
///
/// Los textos que escribe este proyecto ya lo estan. Los que NO: el titulo por omision de un
/// error que nadie redacto (una ruta inexistente, un metodo que la ruta no admite, la sesion que
/// falta, un JSON roto) y los mensajes de fabrica de las reglas de validacion sin
/// <c>WithMessage</c>. La pantalla muestra los dos (<c>frontend/src/api/cliente.ts</c>).
///
/// Por que no se veia: el PC de desarrollo tiene Windows en castellano y FluentValidation
/// respondia en castellano. La imagen de Render no fija idioma, la cultura es la invariante, y
/// respondia «'Correo' must not be empty.». Lo vigila <c>TextosEnCastellanoPruebas</c>, que pide
/// con la cultura invariante, como en Render.
/// </summary>
public static class TextosEnCastellano
{
    /// <summary>
    /// FluentValidation trae sus mensajes traducidos; aqui se elige el castellano para siempre,
    /// sin depender del idioma de la maquina. Global y estatico porque asi lo resuelve la
    /// biblioteca: se llama una vez, al arrancar.
    ///
    /// Devuelve <c>false</c> en vez de lanzar si la cultura no existe. En modo de globalizacion
    /// invariante (las imagenes alpine y chiseled de .NET lo traen encendido) cualquier cultura con
    /// nombre lanza <c>CultureNotFoundException</c>, incluso con <c>predefinedOnly: false</c>, y la API
    /// NO ARRANCARIA por un asunto de textos. Medido el 17-nov-2026 corriendo las pruebas con
    /// <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1</c>. Mejor mensajes en ingles que no tener API:
    /// quien llama lo avisa en el registro. La imagen de hoy (aspnet:8.0, Debian) trae ICU.
    /// </summary>
    public static bool FijarIdiomaDeValidacion()
    {
        try
        {
            ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("es");
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Solo los estados que la API puede devolver sin que nadie haya escrito el titulo. Los que
    /// escriben <see cref="Respuestas"/> y los filtros ya vienen en castellano y no se tocan.
    /// </summary>
    private static readonly Dictionary<int, string> Titulos = new()
    {
        [400] = "Petición incorrecta",
        [401] = "No autorizado",
        [403] = "Acceso denegado",
        [404] = "No encontrado",
        [405] = "Método no permitido",
        [406] = "Formato no aceptado",
        [408] = "La petición tardó demasiado",
        [409] = "Conflicto",
        [412] = "Versión desactualizada",
        [413] = "Petición demasiado grande",
        [415] = "Tipo de contenido no admitido",
        [422] = "Regla de negocio",
        [429] = "Demasiadas peticiones",
        [500] = "Error del servidor",
        [502] = "Servicio no disponible",
        [503] = "Servicio no disponible",
        [504] = "El servicio tardó demasiado",
    };

    /// <summary>El unico titulo de fabrica que no es la frase del estado HTTP.</summary>
    private const string TituloDeFabricaDel500 = "An error occurred while processing your request.";

    /// <summary>
    /// Se encadena DESPUES de lo que ya hubiera (el <c>idPeticion</c>), sin reemplazarlo. Solo
    /// cambia un titulo que es el de fabrica: si alguien escribio uno, manda el suyo.
    /// </summary>
    public static void Configurar(ProblemDetailsOptions opciones)
    {
        var previo = opciones.CustomizeProblemDetails;
        opciones.CustomizeProblemDetails = c =>
        {
            previo?.Invoke(c);
            var problema = c.ProblemDetails;
            var estado = problema.Status ?? c.HttpContext.Response.StatusCode;
            var esDeFabrica = problema.Title is null
                || problema.Title == ReasonPhrases.GetReasonPhrase(estado)
                || problema.Title == TituloDeFabricaDel500;
            if (esDeFabrica && Titulos.TryGetValue(estado, out var titulo))
                problema.Title = titulo;
        };
    }
}
