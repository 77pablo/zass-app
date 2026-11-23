using Microsoft.Extensions.Logging;
using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;

namespace Zass.Aplicacion.Identidad;

/// <summary>
/// Auditoría 22-nov-2026. Borra los archivos HUÉRFANOS del almacenamiento: los que quedaron
/// guardados pero ninguna fila de la base referencia (por ejemplo, si la base falló justo después
/// de subir el archivo y el borrado de compensación tampoco pudo). Lo corre el planificador una
/// vez al día.
///
/// <para>
/// Es un trabajo que borra, así que cada decisión va hacia el lado seguro:
/// </para>
/// <list type="number">
/// <item>Primero se lee de la base lo que está EN USO. Si eso falla no se borra nada: sin la
/// lista, todo parecería huérfano.</item>
/// <item>Solo se miran los prefijos de la aplicación (<c>fotos/</c> y <c>documentos/</c>) y solo
/// claves con la forma que arma ZASS!. Lo demás que haya en el balde no existe para el barrido.</item>
/// <item>Solo se borra lo más viejo que el margen (24 h por omisión): una subida en curso guarda
/// el archivo ANTES de escribir la fila, y esa ventana no puede contar como huérfano.</item>
/// <item>Hay un tope de intentos por corrida: un error no barre todo de golpe.</item>
/// </list>
/// El resumen son números, nunca claves ni datos de nadie.
/// </summary>
public sealed class ServicioBarridoDeArchivos
{
    public static readonly TimeSpan MargenPorOmision = TimeSpan.FromHours(24);
    public const int TopePorOmision = 500;

    /// <summary>Las únicas carpetas que el barrido toca.</summary>
    private static readonly string[] Prefijos =
        { TiposDeArchivo.CarpetaFotos + "/", TiposDeArchivo.CarpetaDocumentos + "/" };

    private readonly IRepositorioClavesDeArchivos _claves;
    private readonly IAlmacenDeArchivos _almacen;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IAuditor _auditor;
    private readonly IRelojSistema _reloj;
    private readonly ILogger<ServicioBarridoDeArchivos> _log;

    public ServicioBarridoDeArchivos(
        IRepositorioClavesDeArchivos claves, IAlmacenDeArchivos almacen, IUnidadDeTrabajo unidad,
        IAuditor auditor, IRelojSistema reloj, ILogger<ServicioBarridoDeArchivos> log)
    {
        _claves = claves;
        _almacen = almacen;
        _unidad = unidad;
        _auditor = auditor;
        _reloj = reloj;
        _log = log;
    }

    /// <summary>Todas las claves que la base referencia, con la foto chica de cada foto de perfil.</summary>
    internal static HashSet<string> ConjuntoEnUso(ClavesDeArchivosEnUso uso)
    {
        var enUso = new HashSet<string>(StringComparer.Ordinal);
        foreach (var url in uso.UrlsDeFotoDePerfil)
        {
            var grande = ServicioFotoDePerfil.ClaveDe(url);
            if (grande is null) continue;
            enUso.Add(grande);
            enUso.Add(TiposDeArchivo.ClaveChica(grande));
        }
        foreach (var c in uso.ClavesDeFotosDeTrabajo) enUso.Add(c);
        foreach (var c in uso.ClavesDeDocumentos) enUso.Add(c);
        return enUso;
    }

    public async Task<string> CorrerAsync(
        TimeSpan margen, int tope, CancellationToken ct = default)
    {
        // 1. Lo que está en uso. Sin esta lista no se borra nada.
        HashSet<string> enUso;
        try
        {
            enUso = ConjuntoEnUso(await _claves.EnUsoAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError("Barrido de archivos: no se pudo leer la base ({Tipo}); no se borró nada.", ex.GetType().Name);
            return "archivos huérfanos: no se pudo leer la base, nada borrado";
        }

        var ahora = _reloj.Ahora;
        var limite = ahora - margen;
        var revisados = 0;
        var intentos = 0;
        var borrados = 0;
        var topeAlcanzado = false;

        try
        {
            foreach (var prefijo in Prefijos)
            {
                await foreach (var archivo in _almacen.ListarAsync(prefijo, ct))
                {
                    // Dos candados más, por si un almacén devolviera de más.
                    if (!archivo.Clave.StartsWith(prefijo, StringComparison.Ordinal)) continue;
                    if (!TiposDeArchivo.EsClaveValida(archivo.Clave)) continue;

                    revisados++;
                    if (enUso.Contains(archivo.Clave)) continue;
                    if (archivo.UltimaModificacionUtc > limite) continue;   // posible subida en curso

                    if (intentos >= tope)
                    {
                        topeAlcanzado = true;
                        break;
                    }

                    intentos++;
                    var r = await _almacen.BorrarAsync(archivo.Clave, CancellationToken.None);
                    if (r.Exito) borrados++;
                }
                if (topeAlcanzado) break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Lo ya borrado se anota igual; lo que falta lo hará la próxima corrida.
            _log.LogError("Barrido de archivos: falló el almacenamiento ({Tipo}); se corta.", ex.GetType().Name);
        }

        if (topeAlcanzado)
            _log.LogWarning("Barrido de archivos: se alcanzó el tope de {Tope} por corrida; se corta.", tope);

        if (borrados > 0)
        {
            _auditor.Registrar(Auditar.Entrada(
                "barrer_archivos_huerfanos", "almacenamiento", null, ahora,
                contexto: new { revisados, borrados, topeAlcanzado }));
            await _unidad.GuardarAsync(ct);
        }

        return $"archivos huérfanos: {revisados} revisados, {borrados} borrados"
               + (topeAlcanzado ? " (tope alcanzado)" : "");
    }
}
