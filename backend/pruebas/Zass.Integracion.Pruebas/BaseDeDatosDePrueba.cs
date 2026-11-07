using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Zass.Infraestructura.Datos;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// La conexion a la instancia local de PostgreSQL. No usa una base en memoria a
/// proposito: lo que estas pruebas comprueban es justamente lo que una base en memoria
/// no tiene, es decir los indices unicos, las restricciones CHECK, la columna geography
/// generada y el token de concurrencia.
/// </summary>
public static class Conexion
{
    private static readonly Lazy<string> _cadena = new(Resolver);

    public static string Cadena => _cadena.Value;

    private static string Resolver()
    {
        var delEntorno = Environment.GetEnvironmentVariable("ZASS_CADENA_CONEXION");
        if (!string.IsNullOrWhiteSpace(delEntorno)) return delEntorno;

        // Sin variable de entorno se arma con la clave local. El archivo esta en
        // .gitignore, asi que esto solo funciona en la maquina donde vive el cluster.
        var clave = ArchivoDeClave()
            ?? throw new InvalidOperationException(
                "No hay ZASS_CADENA_CONEXION ni se encontro .clave-pg.txt. "
                + "Levanta la base con iniciar-postgres.bat y exporta la variable.");

        return $"Host=127.0.0.1;Port=5433;Database=zass;Username=zass;Password={clave}";
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
