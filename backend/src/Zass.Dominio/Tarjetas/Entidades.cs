using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Dominio.Tarjetas;

public enum DestinoTarjeta { Activacion, Ficha, NoValida, PerfilNoDisponible }

/// <summary>CL-24 · TBL-25. El puente fisico hacia el perfil.</summary>
public class Tarjeta
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Segmento unico de la URL estable. Nunca se reutiliza (RN-S6-03).</summary>
    public string UrlEstable { get; set; } = "";
    public Guid? PerfilPrestadorId { get; set; }
    /// <summary>Hash con sal. Nunca el codigo.</summary>
    public string? HashCodigoActivacion { get; set; }
    public EstadoTarjeta Estado { get; set; } = EstadoTarjeta.Emitida;
    public Guid? TarjetaSucesoraId { get; set; }
    public DateTime EmitidaEn { get; set; } = DateTime.UtcNow;
    public DateTime? CodigoVenceEn { get; set; }
    public DateTime? ActivadaEn { get; set; }
    public DateTime? DesactivadaEn { get; set; }
    public short IntentosFallidos { get; set; }
    public DateTime? BloqueadaHasta { get; set; }

    public PerfilPrestador? PerfilPrestador { get; set; }
    public List<EscaneoTarjeta> Escaneos { get; set; } = new();

    private const string Alfabeto = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// 12 caracteres de un alfabeto de 31 sin caracteres confundibles: unos 59 bits.
    /// Generador criptografico, nunca Random.
    /// </summary>
    public static string GenerarCodigo()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
        var c = bytes.Select(b => Alfabeto[b % Alfabeto.Length]).ToArray();
        return new string(c[..4]) + "-" + new string(c[4..8]) + "-" + new string(c[8..]);
    }

    public static string GenerarUrlEstable()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        var c = bytes.Select(b => Alfabeto[b % Alfabeto.Length]).ToArray();
        return new string(c[..4]) + "-" + new string(c[4..]);
    }

    /// <summary>
    /// Devuelve un destino, no una URL: obliga a tratar los cuatro desenlaces y evita
    /// que una tarjeta desactivada filtre de quien fue (RN-S6-06).
    /// </summary>
    public DestinoTarjeta Resolver()
    {
        if (Estado is EstadoTarjeta.Desactivada or EstadoTarjeta.Reemplazada)
            return DestinoTarjeta.NoValida;
        if (Estado == EstadoTarjeta.Emitida) return DestinoTarjeta.Activacion;
        if (PerfilPrestador is null || PerfilPrestador.Estado != EstadoPerfil.Publicado)
            return DestinoTarjeta.PerfilNoDisponible;
        return DestinoTarjeta.Ficha;
    }

    public void Desactivar()
    {
        Estado = EstadoTarjeta.Desactivada;
        DesactivadaEn = DateTime.UtcNow;
        HashCodigoActivacion = null;
    }
}

/// <summary>
/// CL-25 · TBL-26. No tiene columnas de identidad (R-20): la decision de privacidad
/// vive en la estructura, no en la buena memoria del equipo.
/// </summary>
public class EscaneoTarjeta
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TarjetaId { get; set; }
    public MedioEscaneo Medio { get; set; }
    public DateTime OcurridoEn { get; set; } = DateTime.UtcNow;
    public bool DerivoEnContacto { get; set; }

    public Tarjeta? Tarjeta { get; set; }
}
