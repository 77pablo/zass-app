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

    /// <summary>
    /// Resolvedor global de nombres para FluentValidation. Convierte los nombres de propiedad
    /// de C# (PascalCase, sin tildes) a nombres en castellano legibles.
    ///
    /// Reglas:
    /// - Se parte en las mayúsculas: "ContrasenaNueva" → "Contrasena" + "Nueva".
    /// - Se aplica la tabla de tildes a cada palabra.
    /// - Se junta todo en minúsculas con espacios.
    /// - Las siglas (Rut, Url, Nfc, Id) van en mayúsculas: "RUT", "URL", "NFC", "ID".
    /// </summary>
    public static void ConfigurarResolvedor()
    {
        ValidatorOptions.Global.DisplayNameResolver = (tipo, _, _) =>
        {
            // Si el tipo no es una propiedad de validador, devolver null para que FluentValidation
            // use el nombre por defecto.
            if (tipo.DeclaringType is null)
                return null;

            var nombre = tipo.Name;
            if (string.IsNullOrEmpty(nombre))
                return null;

            // Tabla de tildes (del plan de T5, actualización 17-nov, ampliada con el inventario):
            var tildes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Tabla base del plan:
                ["Contrasena"] = "contraseña",
                ["Direccion"] = "dirección",
                ["Descripcion"] = "descripción",
                ["Calificacion"] = "calificación",
                ["Telefono"] = "teléfono",
                ["Codigo"] = "código",
                ["Numero"] = "número",
                ["Pagina"] = "página",
                ["Ubicacion"] = "ubicación",
                ["Razon"] = "razón",
                ["Region"] = "región",
                ["Duracion"] = "duración",
                ["Informacion"] = "información",
                ["Categoria"] = "categoría",
                ["Titulo"] = "título",
                ["Credito"] = "crédito",
                ["Ano"] = "año",
                ["Dia"] = "día",
                ["Señal"] = "señal",
                ["Comun"] = "común",
                // Agregadas del inventario de validadores:
                ["Publico"] = "público",
                ["Interes"] = "interés",
                ["Cupon"] = "cupón",
                ["Servicio"] = "servicio",
                ["Prestador"] = "prestador",
                ["Bloque"] = "bloque",
                ["Nuevo"] = "nuevo",
                ["Nota"] = "nota",
                ["Buscador"] = "buscador",
                ["Entidad"] = "entidad",
                ["Nombre"] = "nombre",
                ["Vence"] = "vence",
                ["Obtenida"] = "obtenida",
                ["Plan"] = "plan",
                ["Desde"] = "desde",
                ["Hasta"] = "hasta",
                ["Fin"] = "fin",
                ["Inicio"] = "inicio",
                ["Semana"] = "semana",
                ["Zona"] = "zona",
                ["Radio"] = "radio",
                ["Preferido"] = "preferido",
                ["Sitio"] = "sitio",
                ["Web"] = "web",
                ["Correo"] = "correo",
                ["Whatsapp"] = "whatsapp",
                ["Longitud"] = "longitud",
                ["Latitud"] = "latitud",
                ["Lat"] = "lat",
                ["Lon"] = "lon",
                ["Cursor"] = "cursor",
                ["Count"] = "count",
                ["Version"] = "versión",
                ["Rubro"] = "rubro",
                ["Descripcion"] = "descripción",
                ["Tipo"] = "tipo",
                ["Resultado"] = "resultado",
                ["Detalle"] = "detalle",
                ["Decision"] = "decisión",
                ["Motivo"] = "motivo",
                ["Texto"] = "texto",
                ["Comentario"] = "comentario",
                ["Aspectos"] = "aspectos",
                ["Contacto"] = "contacto",
                ["Perfil"] = "perfil",
                ["Origen"] = "origen",
                ["Canal"] = "canal",
                ["Token"] = "token",
                ["ContrasenaActual"] = "contraseña actual",
                // Siglas que van en mayúsculas:
                ["Id"] = "ID",
                ["Url"] = "URL",
                ["Nfc"] = "NFC",
                ["Rut"] = "RUT",
                ["Km"] = "km",
            };

            // Partir el nombre en las mayúsculas.
            var palabras = PartirEnMayusculas(nombre);

            // Traducir cada palabra.
            var traducidas = new List<string>();
            foreach (var palabra in palabras)
            {
                if (tildes.TryGetValue(palabra, out var traduccion))
                    traducidas.Add(traduccion);
                else
                    // Si no está en la tabla, dejar en minúsculas tal cual.
                    traducidas.Add(palabra.ToLowerInvariant());
            }

            // Juntar con espacios.
            return string.Join(" ", traducidas);
        };
    }

    /// <summary>
    /// Parte un nombre PascalCase en palabras separadas por las mayúsculas.
    /// Ej: "ContrasenaNueva" → ["Contrasena", "Nueva"]
    ///     "PerfilId" → ["Perfil", "Id"]
    /// </summary>
    private static List<string> PartirEnMayusculas(string nombre)
    {
        var palabras = new List<string>();
        var actual = new System.Text.StringBuilder();

        for (var i = 0; i < nombre.Length; i++)
        {
            var c = nombre[i];
            if (char.IsUpper(c) && actual.Length > 0)
            {
                // Nueva palabra.
                palabras.Add(actual.ToString());
                actual.Clear();
            }
            actual.Append(c);
        }

        if (actual.Length > 0)
            palabras.Add(actual.ToString());

        return palabras;
    }
}
