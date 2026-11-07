using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Zass.Infraestructura.Datos;

/// <summary>
/// Solo la usan las herramientas de EF Core al generar migraciones.
/// Lee la cadena de ZASS_CADENA_CONEXION; si falta, no inventa una: falla.
/// </summary>
public class FabricaDeDisenio : IDesignTimeDbContextFactory<ZassDbContext>
{
    public ZassDbContext CreateDbContext(string[] args)
    {
        var cadena = Environment.GetEnvironmentVariable("ZASS_CADENA_CONEXION");
        if (string.IsNullOrWhiteSpace(cadena))
            throw new InvalidOperationException(
                "Falta la variable de entorno ZASS_CADENA_CONEXION.");

        var opciones = new DbContextOptionsBuilder<ZassDbContext>()
            .UseNpgsql(cadena, o => o.MigrationsAssembly("Zass.Infraestructura"))
            .Options;

        return new ZassDbContext(opciones);
    }
}
