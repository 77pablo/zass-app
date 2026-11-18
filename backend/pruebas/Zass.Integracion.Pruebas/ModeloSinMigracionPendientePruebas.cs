using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zass.Infraestructura.Datos;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Un cambio en el modelo de EF (una propiedad, un índice, un largo máximo) sin su migración
/// pasa todas las pruebas, porque las pruebas crean la base desde las migraciones y el código
/// no la mira. Lo descubre `migrar-neon.yml`, o producción, cuando ya es tarde. Esta prueba lo
/// detecta en el CI: compara el modelo con el último ModelSnapshot.
/// </summary>
public class ModeloSinMigracionPendientePruebas : IAsyncLifetime
{
    private readonly AplicacionConProxy _app = new();

    public Task InitializeAsync() { _app.CreateClient(); return Task.CompletedTask; }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public void El_modelo_no_tiene_cambios_sin_migracion()
    {
        using var alcance = _app.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<ZassDbContext>();

        Assert.False(contexto.Database.HasPendingModelChanges(),
            "El modelo de EF cambio y no hay migracion. Crea una con: "
            + "dotnet ef migrations add <Nombre> --project src/Zass.Infraestructura");
    }
}
