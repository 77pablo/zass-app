using System.Security.Cryptography;
using System.Text;
using Zass.Aplicacion.Identidad;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Pruebas.Dobles;

/// <summary>El reloj que las pruebas mueven a mano: "pasaron 24 h" sin esperar 24 horas.</summary>
public sealed class RelojFijo : IRelojSistema
{
    public DateTime Ahora { get; private set; }
    public RelojFijo(DateTime? inicio = null) =>
        Ahora = inicio ?? new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
    public void Avanzar(TimeSpan cuanto) => Ahora = Ahora.Add(cuanto);
}

/// <summary>
/// Hash de juguete, legible a proposito: la prueba puede comprobar que lo guardado NO es
/// la contrasena en claro sin depender de Argon2id, que se verifica en integracion.
/// </summary>
public sealed class HashDeJuguete : IHashContrasenas
{
    public int LlamadasHash { get; private set; }
    public string Hash(string contrasena)
    {
        LlamadasHash++;
        return "hash:" + contrasena.Length + ":" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contrasena)));
    }
    public bool Verifica(string contrasena, string hash) => Hash(contrasena) == hash;
}

/// <summary>Secretos deterministas, para poder afirmar sobre el token emitido.</summary>
public sealed class SecretosDeterministas : IGeneradorSecretos
{
    private int _n;
    public List<string> Emitidos { get; } = new();

    public string NuevoToken()
    {
        var t = $"token-{++_n:000}";
        Emitidos.Add(t);
        return t;
    }

    public string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed class ColaEnMemoria : INotificaciones
{
    public List<Notificacion> Encoladas { get; } = new();
    public void Encolar(Notificacion aviso) => Encoladas.Add(aviso);

    public Task<bool> ExisteAsync(string claveIdempotencia, CancellationToken ct = default) =>
        Task.FromResult(Encoladas.Any(n => n.ClaveIdempotencia == claveIdempotencia));

    public Task<DateTime?> ProgramadaParaAsync(string claveIdempotencia, CancellationToken ct = default) =>
        Task.FromResult(Encoladas.Where(n => n.ClaveIdempotencia == claveIdempotencia)
            .Select(n => (DateTime?)n.ProgramadaPara).FirstOrDefault());

    public Task<int> AnularRecordatoriosAsync(Guid citaId, CancellationToken ct = default)
    {
        var vivos = Encoladas.Where(n => n.CitaId == citaId && n.Estado == EstadoEnvio.Programada
            && n.Tipo is TipoNotificacion.Recordatorio24h or TipoNotificacion.Recordatorio2h).ToList();
        foreach (var n in vivos) n.Anular();
        return Task.FromResult(vivos.Count);
    }

    public Task<Notificacion?> UltimoAsync(Guid destinatarioId, TipoNotificacion tipo, CancellationToken ct = default) =>
        Task.FromResult(Encoladas
            .Where(n => n.DestinatarioId == destinatarioId && n.Tipo == tipo)
            .OrderByDescending(n => n.ProgramadaPara).FirstOrDefault());

    /// <summary>Como la base: con la clave ya usada no se agrega y se devuelve falso.</summary>
    public Task<bool> EncolarSiNoExisteAsync(Notificacion aviso, CancellationToken ct = default)
    {
        if (Encoladas.Any(n => n.ClaveIdempotencia == aviso.ClaveIdempotencia)) return Task.FromResult(false);
        Encoladas.Add(aviso);
        return Task.FromResult(true);
    }
}

public sealed class AuditoriaEnMemoria : IAuditor
{
    public List<RegistroAuditoria> Registros { get; } = new();
    public void Registrar(RegistroAuditoria entrada) => Registros.Add(entrada);
}

/// <summary>Cifrado reversible de prueba. No es seguro; solo prueba que no se guarda en claro.</summary>
public sealed class CifradoDeJuguete : ICifradoDeDatos
{
    public byte[] Cifrar(string texto) =>
        Encoding.UTF8.GetBytes(texto).Select(b => (byte)(b ^ 0x5A)).ToArray();
    public string Descifrar(byte[] datos) =>
        Encoding.UTF8.GetString(datos.Select(b => (byte)(b ^ 0x5A)).ToArray());
}

/// <summary>Doble que dice "filtrada" para una contrasena especifica (por defecto "password").</summary>
public sealed class ContrasenasFiltradasFiltrada : IContrasenasFiltradas
{
    private readonly string _contrasenaFiltrada;

    public ContrasenasFiltradasFiltrada(string contrasenaFiltrada = "password")
    {
        _contrasenaFiltrada = contrasenaFiltrada;
    }

    public Task<bool> EstaFiltradaAsync(string contrasena, CancellationToken ct = default)
        => Task.FromResult(contrasena == _contrasenaFiltrada);
}

/// <summary>Doble que nunca dice "filtrada" (equivale a la opcion en false).</summary>
public sealed class ContrasenasFiltradasNunca : IContrasenasFiltradas
{
    public Task<bool> EstaFiltradaAsync(string contrasena, CancellationToken ct = default)
        => Task.FromResult(false);
}

/// <summary>Doble de IAlertasAdministrador que captura las alertas para las pruebas.</summary>
public sealed class AlertasAdministradorEnMemoria : IAlertasAdministrador
{
    public List<AlertaTarjetaBloqueada> Alertas { get; } = new();
    public Task AvisarTarjetaBloqueadaAsync(AlertaTarjetaBloqueada alerta, CancellationToken ct = default)
    {
        Alertas.Add(alerta);
        return Task.CompletedTask;
    }
}

/// <summary>Repositorio de auditoria en memoria para pruebas.</summary>
public sealed class RepositorioAuditoriaEnMemoria : IRepositorioAuditoria
{
    private readonly List<RegistroAuditoria> _registros = new();
    private long _nextId = 1;

    public List<RegistroAuditoria> Registros => _registros;

    public Task<IReadOnlyList<RegistroAuditoria>> ConsultarAsync(
        string? entidad, Guid? entidadId, Guid? actorId,
        DateTime? antesDe, long? antesDeId, int limite, CancellationToken ct = default)
    {
        var consulta = _registros.AsQueryable();

        if (!string.IsNullOrWhiteSpace(entidad) && entidadId.HasValue)
        {
            consulta = consulta.Where(r => r.Entidad == entidad && r.EntidadId == entidadId);
        }
        else if (actorId.HasValue)
        {
            consulta = consulta.Where(r => r.ActorId == actorId);
        }

        if (antesDe.HasValue && antesDeId.HasValue)
        {
            consulta = consulta.Where(r => r.OcurridoEn < antesDe || (r.OcurridoEn == antesDe && r.Id < antesDeId.Value));
        }
        else if (antesDe.HasValue)
        {
            consulta = consulta.Where(r => r.OcurridoEn < antesDe);
        }

        consulta = consulta.OrderByDescending(r => r.OcurridoEn).ThenByDescending(r => r.Id);

        return Task.FromResult<IReadOnlyList<RegistroAuditoria>>(
            consulta.Take(limite + 1).ToList());
    }

    public Task<int> ContarActivacionesFallidasAsync(
        Guid tarjetaId, DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        return Task.FromResult(_registros.Count(r =>
            r.Entidad == "Tarjeta"
            && r.EntidadId == tarjetaId
            && r.Accion == "TarjetaActivacionFallida"
            && r.OcurridoEn >= desde
            && r.OcurridoEn <= hasta));
    }

    public Task<IReadOnlyList<DateTime>> ObtenerFechasDeFallosAsync(
        Guid tarjetaId, DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<DateTime>>(_registros
            .Where(r => r.Entidad == "Tarjeta"
                     && r.EntidadId == tarjetaId
                     && r.Accion == "TarjetaActivacionFallida"
                     && r.OcurridoEn >= desde
                     && r.OcurridoEn <= hasta)
            .OrderBy(r => r.OcurridoEn)
            .Select(r => r.OcurridoEn)
            .ToList());
    }

    internal void Agregar(RegistroAuditoria r)
    {
        r.Id = _nextId++;
        _registros.Add(r);
    }
}

/// <summary>Doble de IAlmacenDeArchivos que no guarda nada.</summary>
public sealed class AlmacenApagado : IAlmacenDeArchivos
{
    public Task<Resultado> GuardarAsync(string clave, Stream contenido, string tipoMime, CancellationToken ct = default)
        => Task.FromResult(Resultado.Falla(Fallas.AlmacenamientoNoDisponible, "Almacenamiento deshabilitado en pruebas."));
    public Task<ArchivoAbierto?> AbrirAsync(string clave, CancellationToken ct = default) => Task.FromResult<ArchivoAbierto?>(null);
    public Task<Resultado> BorrarAsync(string clave, CancellationToken ct = default) => Task.FromResult(Resultado.Ok());
}

/// <summary>Doble de IProcesadorDeImagenes que devuelve la imagen sin tocar.</summary>
public sealed class ProcesadorDeImagenesDePrueba : IProcesadorDeImagenes
{
    public Task<Resultado<FotoPreparada>> PrepararFotoAsync(Stream imagen, int ladoGrande, int ladoChico, CancellationToken ct = default)
    {
        var bytes = new byte[imagen.Length];
        imagen.Position = 0;
        imagen.Read(bytes, 0, bytes.Length);
        return Task.FromResult(Resultado.Ok(new FotoPreparada(bytes, bytes)));
    }

    public Task<Resultado<byte[]>> LimpiarImagenAsync(Stream imagen, TipoArchivo tipo, CancellationToken ct = default)
    {
        var bytes = new byte[imagen.Length];
        imagen.Position = 0;
        imagen.Read(bytes, 0, bytes.Length);
        return Task.FromResult(Resultado.Ok(bytes));
    }

    public Task<Resultado<byte[]>> PrepararFotoTrabajoAsync(Stream imagen, int ladoMaximo, CancellationToken ct = default)
    {
        var bytes = new byte[imagen.Length];
        imagen.Position = 0;
        imagen.Read(bytes, 0, bytes.Length);
        return Task.FromResult(Resultado.Ok(bytes));
    }
}

/// <summary>Doble de IServicioFotosDeTrabajo que devuelve lista vacia.</summary>
public sealed class ServicioFotosDeTrabajoFalso : IServicioFotosDeTrabajo
{
    public Task<Resultado<FotoTrabajoPropiaDto>> SubirAsync(Guid usuarioId, Stream contenido, long largoDeclarado, CancellationToken ct = default)
        => Task.FromResult(Resultado.Falla<FotoTrabajoPropiaDto>(Fallas.ReglaDeNegocio, "Falso en pruebas."));
    public Task<Resultado<MisFotosDeTrabajoDto>> MisFotosAsync(Guid usuarioId, CancellationToken ct = default)
        => Task.FromResult(Resultado.Falla<MisFotosDeTrabajoDto>(Fallas.ReglaDeNegocio, "Falso en pruebas."));
    public Task<Resultado> OrdenarAsync(Guid usuarioId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
        => Task.FromResult(Resultado.Falla(Fallas.ReglaDeNegocio, "Falso en pruebas."));
    public Task<Resultado> BorrarAsync(Guid usuarioId, Guid fotoId, CancellationToken ct = default)
        => Task.FromResult(Resultado.Falla(Fallas.ReglaDeNegocio, "Falso en pruebas."));
    public Task<IReadOnlyList<FotoTrabajoPublicaDto>> ObtenerParaFichaAsync(Guid perfilPrestadorId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FotoTrabajoPublicaDto>>(Array.Empty<FotoTrabajoPublicaDto>());
}
