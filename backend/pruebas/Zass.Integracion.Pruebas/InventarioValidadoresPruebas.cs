using System.Reflection;
using FluentValidation;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// Inventario por reflexion de todas las propiedades validadas.
/// Se usa para armar el diccionario de nombres en castellano en T5.
/// </summary>
public class InventarioValidadoresPruebas
{
    [Fact]
    public void Listar_todos_los_validadores_registrados()
    {
        // Se obtienen todos los IValidator del ensamblado que contiene los validadores de API.
        var assembly = typeof(Zass.Api.Configuracion.ValidadorVerificarCorreo).Assembly;
        var validadores = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i => 
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)))
            .ToList();

        var propiedades = new List<string>();
        foreach (var validador in validadores)
        {
            // Se crea una instancia del validador para poder llamar a CreateDescriptor().
            var instancia = Activator.CreateInstance(validador);
            if (instancia is IValidator v)
            {
                var descriptor = v.CreateDescriptor();
                var miembros = descriptor.GetMembersWithValidators();
                foreach (var miembro in miembros)
                {
                    // Se toma el nombre de la propiedad (es la clave del agrupamiento).
                    var nombre = miembro.Key;
                    if (!string.IsNullOrEmpty(nombre) && !propiedades.Contains(nombre))
                        propiedades.Add(nombre);
                }
            }
        }

        // Se ordenan alfabeticamente para facilitar la lectura.
        propiedades.Sort();

        // Se escribe el inventario en la salida de la prueba.
        Console.WriteLine("=== INVENTARIO DE PROPIEDADES VALIDADAS ===");
        foreach (var p in propiedades)
            Console.WriteLine(p);
        Console.WriteLine($"=== TOTAL: {propiedades.Count} propiedades ===");

        // La prueba pasa siempre: solo se usa para listar.
        Assert.True(propiedades.Count > 0, "No se encontraron propiedades validadas.");
    }
}
