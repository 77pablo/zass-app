using Zass.Aplicacion.Comun;

namespace Zass.Aplicacion.Pruebas.Identidad;

/// <summary>
/// El RUT (registro completo, 20-nov-2026). Los digitos verificadores esperados estan
/// calculados a mano con modulo 11, no con la misma funcion que se prueba.
/// </summary>
public class RutPruebas
{
    [Theory]
    [InlineData(11111111, '1')]
    [InlineData(12345678, '5')]
    [InlineData(76086428, '5')]
    [InlineData(8765432, 'K')]
    [InlineData(10000004, '0')]
    public void El_digito_verificador_es_el_del_modulo_11(int cuerpo, char esperado) =>
        Assert.Equal(esperado, Rut.DigitoVerificador(cuerpo));

    [Theory]
    [InlineData("12.345.678-5", "12345678-5")]
    [InlineData("12345678-5", "12345678-5")]
    [InlineData("123456785", "12345678-5")]
    [InlineData(" 12 345 678 - 5 ", "12345678-5")]
    [InlineData("8.765.432-k", "8765432-K")]
    public void Se_guarda_siempre_con_la_misma_forma(string escrito, string guardado) =>
        Assert.Equal(guardado, Rut.Normalizar(escrito));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12.345.678-4")]    // verificador equivocado
    [InlineData("12.345.678-X")]
    [InlineData("1-9")]             // cuerpo demasiado corto
    [InlineData("123456789-0")]     // cuerpo de 9 digitos
    [InlineData("12a45678-5")]
    public void Lo_que_no_es_un_RUT_no_se_normaliza(string? escrito) =>
        Assert.Null(Rut.Normalizar(escrito));
}
