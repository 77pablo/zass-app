using Microsoft.EntityFrameworkCore;
using Zass.Infraestructura.Datos;

namespace Zass.Api;

/// <summary>
/// Modo `--migrar`: aplica las migraciones pendientes y TERMINA, sin abrir ningún puerto.
///
/// Por qué no migrar al arrancar: con varias réplicas, dos instancias migrando a la vez se
/// pisan; un despliegue que falla a medias deja la base a medio esquema con la versión vieja
/// del código sirviendo encima; y la API necesitaría en producción un usuario de base con
/// permisos de DDL que no debería tener para atender peticiones. Migrar es un paso explícito,
/// con quien lo ejecuta mirando el resultado.
///
/// Es idempotente: sin migraciones pendientes no hace nada y sale con 0.
/// </summary>
public static class Migrador
{
    public const string Bandera = "--migrar";

    public static bool Solicitado(string[] args) =>
        args.Contains(Bandera, StringComparer.OrdinalIgnoreCase);

    public static async Task<int> AplicarAsync(IServiceProvider servicios, ILogger registro)
    {
        try
        {
            using var ambito = servicios.CreateScope();
            var db = ambito.ServiceProvider.GetRequiredService<ZassDbContext>();

            var pendientes = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pendientes.Count == 0)
            {
                registro.LogInformation("Migraciones: la base ya está al día.");
                return 0;
            }

            registro.LogInformation("Migraciones: aplicando {N} pendiente(s): {Lista}",
                pendientes.Count, string.Join(", ", pendientes));
            await db.Database.MigrateAsync();
            registro.LogInformation("Migraciones: listo.");
            return 0;
        }
        catch (Exception ex)
        {
            registro.LogCritical(ex,
                "Migraciones: falló. La base puede haber quedado a medias; revisa antes de desplegar.");
            return 1;
        }
    }
}
