using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.EntityFrameworkCore.Storage;
using Zass.Infraestructura.Datos;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// La conexion a la base de las PRUEBAS, que no es la de desarrollo.
///
/// Las pruebas corrian contra la misma base `zass` que usa quien desarrolla, y eso tenia un
/// costo que se pago varias veces: cualquier dato de ejemplo (prestadores publicados para ver
/// el sitio) hacia fallar las pruebas de la busqueda, que esperan una base sin prestadores
/// visibles, y habia que limpiar a mano con un script. Ahora las pruebas tienen la suya,
/// `zass_pruebas`, y la base de desarrollo puede tener datos de ejemplo sin que nada se rompa.
///
/// No usa una base en memoria a proposito: lo que estas pruebas comprueban es justamente lo
/// que una base en memoria no tiene, es decir los indices unicos, las restricciones CHECK, la
/// columna geography generada y el token de concurrencia.
/// </summary>
public static class Conexion
{
    public const string BaseDePruebas = "zass_pruebas";

    private static readonly Lazy<string> _cadena = new(Preparar);

    public static string Cadena => _cadena.Value;

    private static string Preparar()
    {
        var cadena = Resolver();
        AsegurarLaBase(cadena);
        return cadena;
    }

    private static string Resolver()
    {
        // Variable PROPIA de las pruebas, y no ZASS_CADENA_CONEXION: esa es la de desarrollo
        // (la usan las migraciones), y si las pruebas la leyeran bastaria con tenerla puesta
        // para volver a correr contra la base de verdad sin darse cuenta.
        var delEntorno = Environment.GetEnvironmentVariable("ZASS_CADENA_PRUEBAS");
        if (!string.IsNullOrWhiteSpace(delEntorno)) return delEntorno;

        // Sin variable se arma con la clave local. El archivo esta en .gitignore, asi que esto
        // solo funciona en la maquina donde vive el cluster.
        var clave = ArchivoDeClave()
            ?? throw new InvalidOperationException(
                "No hay ZASS_CADENA_PRUEBAS ni se encontro .clave-pg.txt. "
                + "Levanta la base con iniciar-postgres.bat.");

        return $"Host=127.0.0.1;Port=5433;Database={BaseDePruebas};Username=zass;Password={clave}";
    }

    /// <summary>
    /// Crea la base de pruebas si no existe y le aplica las migraciones. Las migraciones traen
    /// tambien los datos de catalogo (roles, plan de prueba, rubros, servicios y zonas), asi
    /// que una base recien creada queda igual que la de desarrollo, pero sin prestadores.
    /// </summary>
    private static void AsegurarLaBase(string cadena)
    {
        var destino = new NpgsqlConnectionStringBuilder(cadena);
        var nombre = destino.Database ?? "";

        // La barrera que importa: si por un descuido la cadena apuntara a la de desarrollo,
        // las pruebas se negarian en vez de escribir y borrar filas en ella.
        if (string.Equals(nombre, "zass", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Las pruebas de integracion no corren contra la base de desarrollo (zass). "
                + $"Usan {BaseDePruebas}; revisa ZASS_CADENA_PRUEBAS.");

        // El nombre va dentro de un CREATE DATABASE, que no admite parametros.
        if (!System.Text.RegularExpressions.Regex.IsMatch(nombre, "^[a-z0-9_]{1,40}$"))
            throw new InvalidOperationException($"Nombre de base no valido: '{nombre}'.");

        var mantenimiento = new NpgsqlConnectionStringBuilder(cadena) { Database = "postgres" };
        using (var conexion = new NpgsqlConnection(mantenimiento.ConnectionString))
        {
            conexion.Open();
            using var existe = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", conexion);
            existe.Parameters.AddWithValue("n", nombre);
            if (existe.ExecuteScalar() is null)
            {
                using var crear = new NpgsqlCommand($"CREATE DATABASE \"{nombre}\"", conexion);
                crear.ExecuteNonQuery();
            }
        }

        using var ctx = new ZassDbContext(
            new DbContextOptionsBuilder<ZassDbContext>().UseNpgsql(cadena).Options);
        ctx.Database.Migrate();
    }

    private static string? ArchivoDeClave()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null)
        {
            var archivo = Path.Combine(carpeta.FullName, ".clave-pg.txt");
            if (File.Exists(archivo)) return File.ReadAllText(archivo).Trim();
            carpeta = carpeta.Parent;
        }
        return null;
    }
}

/// <summary>
/// Un contexto por prueba, dentro de una transaccion que SIEMPRE se deshace.
///
/// Deshacer en vez de borrar filas tiene una ventaja que importa aqui: las
/// restricciones de la base se evaluan igual dentro de la transaccion, asi que una
/// prueba puede comprobar que un indice unico rechaza de verdad el duplicado, y al
/// terminar la base queda exactamente como estaba.
/// </summary>
public sealed class ContextoDePrueba : IDisposable
{
    public ZassDbContext Ctx { get; }
    private readonly IDbContextTransaction _transaccion;

    public ContextoDePrueba()
    {
        Ctx = Crear();
        _transaccion = Ctx.Database.BeginTransaction();
    }

    public static ZassDbContext Crear() => new(
        new DbContextOptionsBuilder<ZassDbContext>()
            .UseNpgsql(Conexion.Cadena)
            .Options);

    public void Dispose()
    {
        _transaccion.Rollback();
        _transaccion.Dispose();
        Ctx.Dispose();
    }
}
