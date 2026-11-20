using System.Text.Json.Nodes;
using Zass.Api.Configuracion;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// El cuerpo de una respuesta, listo para compararlo byte a byte con otro.
///
/// Las pruebas de «ajena e inexistente dan EXACTAMENTE la misma respuesta» comparan los cuerpos
/// enteros para que nadie pueda distinguir que existe mirando la respuesta. Desde el 17-nov cada
/// error trae su <c>idPeticion</c>, que es aleatorio y distinto en cada peticion: no dice nada de
/// lo que se pidio, pero haria distintos dos cuerpos que en todo lo demas son iguales.
///
/// Esto NO afloja esas pruebas: exige que el identificador este y coincida con la cabecera, lo
/// quita, y devuelve el resto sin tocar, en el mismo orden, para que la comparacion siga siendo
/// de todo lo demas.
/// </summary>
public static class CuerpoComparable
{
    public static async Task<string> De(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync();
        var cuerpo = JsonNode.Parse(texto) as JsonObject
            ?? throw new Xunit.Sdk.XunitException($"El cuerpo no es un objeto JSON: {texto}");

        var cabecera = Assert.Single(respuesta.Headers.GetValues(IdDePeticion.Cabecera));
        Assert.True(cuerpo.TryGetPropertyValue(IdDePeticion.ClaveDelProblema, out var id),
            $"El error no trae {IdDePeticion.ClaveDelProblema}: {texto}");
        Assert.Equal(cabecera, id!.GetValue<string>());

        cuerpo.Remove(IdDePeticion.ClaveDelProblema);
        return cuerpo.ToJsonString();
    }
}
