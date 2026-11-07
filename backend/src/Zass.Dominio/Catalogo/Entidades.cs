using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Dominio.Catalogo;

/// <summary>CL-06 · TBL-07.</summary>
public class Rubro
{
    public short Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? Icono { get; set; }
    public bool Activo { get; set; } = true;

    public List<Servicio> Servicios { get; set; } = new();
}

/// <summary>CL-05 · TBL-08.</summary>
public class Servicio
{
    public int Id { get; set; }
    public short RubroId { get; set; }
    public string Nombre { get; set; } = "";

    /// <summary>
    /// El nombre sin tildes, en minusculas y con los espacios comprimidos. Es la clave
    /// con la que el unico de (rubro_id, nombre_normalizado) decide que dos servicios
    /// son el mismo, y la calcula la aplicacion: dejarselo a lower() ataria la regla al
    /// locale del servidor, que en el cluster local es C y solo plega ASCII.
    /// </summary>
    public string NombreNormalizado { get; set; } = "";
    public string? Descripcion { get; set; }
    public bool EsCatalogo { get; set; } = true;

    public Rubro? Rubro { get; set; }
}

/// <summary>
/// TBL-09. No es una tabla puente pelada: duracion y modo dependen del par
/// prestador-servicio, no de ninguno de los dos por separado (2FN).
/// </summary>
public class PrestadorServicio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PerfilPrestadorId { get; set; }
    public int ServicioId { get; set; }
    public short DuracionMinutos { get; set; } = 30;
    public ModoAgenda ModoAgenda { get; set; } = ModoAgenda.Automatico;
    public int? PrecioReferencia { get; set; }
    public bool Activo { get; set; } = true;

    public PerfilPrestador? PerfilPrestador { get; set; }
    public Servicio? Servicio { get; set; }
}

/// <summary>CL-07 · TBL-10. Lo unico geografico que se publica.</summary>
public class Zona
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Sector { get; set; } = "";
    public double CentroLatitud { get; set; }
    public double CentroLongitud { get; set; }
    public string Comuna { get; set; } = "Temuco";
}

/// <summary>CL-08 · TBL-11. Una sola zona principal por prestador (R-2).</summary>
public class CoberturaPrestador
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PerfilPrestadorId { get; set; }
    public int ZonaId { get; set; }
    public bool EsPrincipal { get; set; }
    public DateTime DesdeEn { get; set; } = DateTime.UtcNow;

    public PerfilPrestador? PerfilPrestador { get; set; }
    public Zona? Zona { get; set; }
}
