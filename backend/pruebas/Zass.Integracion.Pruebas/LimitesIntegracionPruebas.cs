using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zass.Aplicacion.Identidad;
using Zass.Dominio.Identidad;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Los limites de longitud, comprobados donde de verdad importan: en la base. Un limite que
/// solo vive en el validador se salta con cualquier camino que no pase por el.
/// </summary>
public class LimitesIntegracionPruebas : IDisposable
{
    private readonly ContextoDePrueba _prueba = new();
    public void Dispose() => _prueba.Dispose();

    private PerfilPrestador UnPerfil(string descripcion)
    {
        var usuario = new Usuario
        {
            Correo = $"lim-{Guid.NewGuid():N}@zass.local", HashContrasena = "x",
            Nombre = "Prestador de prueba", CorreoVerificado = true,
        };
        _prueba.Ctx.Usuarios.Add(usuario);

        var perfil = new PerfilPrestador
        {
            UsuarioId = usuario.Id,
            NombrePublico = $"Estudio {Guid.NewGuid():N}"[..20],
            Slug = $"estudio-{Guid.NewGuid():N}",
            Descripcion = descripcion,
        };
        _prueba.Ctx.PerfilesPrestador.Add(perfil);
        return perfil;
    }

    /// <summary>
    /// El numero estuvo escrito DOS veces y a mano: 2000 en la constante del validador y 2000
    /// otra vez como literal en el mapeo de la base. Cambiar una sin la otra deja un limite
    /// que miente. Ahora el mapeo lee la constante, y esto lo vigila.
    /// </summary>
    [Fact]
    public void La_columna_de_la_base_usa_el_mismo_limite_que_el_validador()
    {
        var propiedad = _prueba.Ctx.Model
            .FindEntityType(typeof(PerfilPrestador))!
            .FindProperty(nameof(PerfilPrestador.Descripcion))!;

        Assert.Equal(Limites.DescripcionMaxima, propiedad.GetMaxLength());
        Assert.Equal(1500, Limites.DescripcionMaxima);
    }

    [Fact]
    public async Task La_base_acepta_1500_caracteres_de_descripcion()
    {
        UnPerfil(new string('a', 1500));

        await _prueba.Ctx.SaveChangesAsync();   // no lanza
    }

    /// <summary>
    /// Saltandose el servicio y el validador, la base tiene que rechazar el 1501 por su
    /// cuenta. 22001 es string_data_right_truncation.
    /// </summary>
    [Fact]
    public async Task La_base_rechaza_1501_caracteres_de_descripcion()
    {
        UnPerfil(new string('a', 1501));

        var error = await Assert.ThrowsAsync<DbUpdateException>(
            () => _prueba.Ctx.SaveChangesAsync());
        Assert.Equal("22001", (error.InnerException as PostgresException)?.SqlState);
    }
}
