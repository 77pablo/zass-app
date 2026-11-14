using System.Reflection;
using NetArchTest.Rules;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Infraestructura.Datos;

namespace Zass.Arquitectura.Pruebas;

/// <summary>
/// Las reglas de arquitectura del proyecto, escritas como pruebas para que nadie las rompa sin darse cuenta.
///
/// Importan porque el diseño entero depende de ellas: el dominio sin paquetes, los casos de uso que se prueban sin base
/// de datos, y los módulos que se hablan por sus servicios y no por sus tablas. Una sola dependencia de más —un
/// servicio que consulta el repositorio de otro módulo «solo esta vez»— deshace en silencio todo eso, y nada falla hasta
/// que cambia la base. Por eso la regla vive aquí y falla en rojo el día que alguien la rompe.
///
/// LIMITES (se midieron rompiendo las reglas a proposito): la herramienta ve campos, parametros y llamadas, pero NO un
/// `typeof(X)` suelto dentro de un metodo; y una dependencia de la Aplicacion a EF Core ni siquiera compilaria, porque
/// ese proyecto no tiene el paquete (esa frontera la hace cumplir el compilador, y esta prueba la deja escrita).
/// </summary>
public class ArquitecturaPruebas
{
    private static readonly Assembly Dominio = typeof(Resultado).Assembly;
    private static readonly Assembly Aplicacion = typeof(IRelojSistema).Assembly;
    private static readonly Assembly Infraestructura = typeof(ZassDbContext).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static string Reporte(TestResult r) =>
        r.IsSuccessful ? "" : "Rompen la regla: " + string.Join(", ", r.FailingTypeNames ?? Array.Empty<string>());

    // ------------------------------------------------------------- las capas, de adentro hacia afuera

    [Fact]
    public void El_dominio_no_conoce_a_ninguna_otra_capa()
    {
        var r = Types.InAssembly(Dominio).ShouldNot()
            .HaveDependencyOnAny("Zass.Aplicacion", "Zass.Infraestructura", "Zass.Api").GetResult();

        Assert.True(r.IsSuccessful, Reporte(r));
    }

    [Fact]
    public void La_aplicacion_no_conoce_ni_la_infraestructura_ni_la_api()
    {
        var r = Types.InAssembly(Aplicacion).ShouldNot()
            .HaveDependencyOnAny("Zass.Infraestructura", "Zass.Api").GetResult();

        Assert.True(r.IsSuccessful, Reporte(r));
    }

    [Fact]
    public void La_infraestructura_no_conoce_a_la_api()
    {
        var r = Types.InAssembly(Infraestructura).ShouldNot().HaveDependencyOn("Zass.Api").GetResult();

        Assert.True(r.IsSuccessful, Reporte(r));
    }

    // ------------------------------------------------------------- lo que cada capa NO puede usar

    /// <summary>El dominio es C# puro: nada de EF Core, ASP.NET, FluentValidation ni drivers de base de datos.</summary>
    [Fact]
    public void El_dominio_no_usa_marcos_ni_bibliotecas_de_terceros()
    {
        var r = Types.InAssembly(Dominio).ShouldNot().HaveDependencyOnAny(
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "FluentValidation", "Npgsql")
            .GetResult();

        Assert.True(r.IsSuccessful, Reporte(r));
    }

    /// <summary>La exigencia del propio diseño: «Zass.Dominio» no lleva paquetes NuGet.</summary>
    [Fact]
    public void El_proyecto_del_dominio_no_declara_paquetes_NuGet()
    {
        var csproj = File.ReadAllText(RaizDeFuentes("Zass.Dominio", "Zass.Dominio.csproj"));

        Assert.DoesNotContain("PackageReference", csproj);
        Assert.DoesNotContain("ProjectReference", csproj);
    }

    /// <summary>
    /// Los casos de uso no saben de EF Core ni de PostgreSQL: se prueban con listas en memoria, y declaran lo que necesitan
    /// como puertos que la infraestructura implementa.
    /// </summary>
    [Fact]
    public void La_aplicacion_no_usa_EF_Core_ni_Npgsql_ni_ASP_NET()
    {
        var r = Types.InAssembly(Aplicacion).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore").GetResult();

        Assert.True(r.IsSuccessful, Reporte(r));
    }

    /// <summary>Los endpoints hablan con servicios, nunca con el contexto de la base ni con repositorios.</summary>
    [Fact]
    public void Los_endpoints_no_tocan_la_base_ni_los_repositorios()
    {
        var r = Types.InAssembly(Api).That().ResideInNamespace("Zass.Api.Endpoints").ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", "Zass.Infraestructura.Datos")
            .GetResult();
        var repos = Types.InAssembly(Api).That().ResideInNamespace("Zass.Api.Endpoints").GetTypes()
            .Where(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(m => m.GetParameters()).Any(p => p.ParameterType.Name.StartsWith("IRepositorio")))
            .Select(t => t.Name).ToList();

        Assert.True(r.IsSuccessful, Reporte(r));
        Assert.Empty(repos);
    }

    // ------------------------------------------------------------- los módulos se hablan por sus servicios

    /// <summary>
    /// Cada módulo es dueño de sus tablas: solo SU servicio habla con SU repositorio. Los demás consumen el servicio del
    /// módulo (CU-08 usa IServicioCatalogo y IServicioGeografia, no sus repositorios). Si un módulo lee la tabla de otro, el
    /// dia que esa tabla cambie se rompe algo que nadie sabia que dependia de ella.
    /// </summary>
    private static readonly (string Modulo, string[] Propios)[] Modulos =
    {
        ("Busqueda", new[] { "IRepositorioBusqueda" }),
        ("Catalogo", new[] { "IRepositorioCatalogo" }),
        ("Geografia", new[] { "IRepositorioGeografia", "IGeocodificador" }),
        ("Suscripciones", new[] { "IRepositorioSuscripciones" }),
        ("Credenciales", new[] { "IRepositorioCredenciales" }),
        ("Agenda", new[] { "IRepositorioAgenda" }),
        ("Identidad", new[] { "IRepositorioUsuarios", "IRepositorioSesiones", "IRepositorioPerfiles" }),
        ("Tarjetas", new[] { "IRepositorioTarjetas" }),
    };

    public static IEnumerable<object[]> CadaModulo() => Modulos.Select(m => new object[] { m.Modulo });

    [Theory]
    [MemberData(nameof(CadaModulo))]
    public void Un_modulo_no_usa_los_repositorios_de_otro(string modulo)
    {
        var propios = Modulos.Single(m => m.Modulo == modulo).Propios;
        var ajenos = Modulos.Where(m => m.Modulo != modulo).SelectMany(m => m.Propios)
            .Select(n => "Zass.Aplicacion.Puertos." + n).ToArray();

        var r = Types.InAssembly(Aplicacion).That().ResideInNamespace("Zass.Aplicacion." + modulo)
            .ShouldNot().HaveDependencyOnAny(ajenos).GetResult();

        Assert.True(r.IsSuccessful, Reporte(r));
        Assert.NotEmpty(propios);
    }

    // ------------------------------------------------------------- controles: que las reglas puedan fallar

    /// <summary>
    /// Un verde no prueba nada si la regla no puede ponerse en rojo. Estos controles comprueban que el verificador SI ve
    /// las dependencias que existen, para que «ningun tipo rompe la regla» no sea un resultado vacio.
    /// </summary>
    [Fact]
    public void Control_el_verificador_ve_las_dependencias_que_si_existen()
    {
        // `Should()` exige que TODOS los tipos filtrados cumplan: se filtra a los que de verdad deben depender.
        var infraDependeDeAplicacion = Types.InAssembly(Infraestructura).That().HaveNameEndingWith("EfCore")
            .Should().HaveDependencyOn("Zass.Aplicacion").GetResult();
        var aplicacionDependeDeDominio = Types.InAssembly(Aplicacion).That().HaveName("ServicioBusqueda")
            .Should().HaveDependencyOn("Zass.Dominio").GetResult();
        var busquedaUsaSuRepositorio = Types.InAssembly(Aplicacion).That().HaveName("ServicioBusqueda")
            .Should().HaveDependencyOn("Zass.Aplicacion.Puertos.IRepositorioBusqueda").GetResult();

        Assert.True(infraDependeDeAplicacion.IsSuccessful);
        Assert.True(aplicacionDependeDeDominio.IsSuccessful);
        Assert.True(busquedaUsaSuRepositorio.IsSuccessful);
    }

    [Fact]
    public void Control_los_modulos_estan_donde_se_los_busca()
    {
        foreach (var (modulo, _) in Modulos)
        {
            var tipos = Types.InAssembly(Aplicacion).That().ResideInNamespace("Zass.Aplicacion." + modulo).GetTypes();
            Assert.NotEmpty(tipos);
        }
    }

    private static string RaizDeFuentes(string proyecto, string archivo)
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null)
        {
            var candidato = Path.Combine(carpeta.FullName, "src", proyecto, archivo);
            if (File.Exists(candidato)) return candidato;
            carpeta = carpeta.Parent;
        }
        throw new FileNotFoundException($"No encontre src/{proyecto}/{archivo} subiendo desde {AppContext.BaseDirectory}.");
    }
}
