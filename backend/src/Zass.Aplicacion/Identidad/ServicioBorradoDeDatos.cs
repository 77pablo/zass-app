using Microsoft.Extensions.Logging;
using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Credenciales;
using Zass.Aplicacion.Notificaciones;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;

namespace Zass.Aplicacion.Identidad;

/// <summary>
/// CS-04 (Ley 21.719): que eliminar la cuenta borre de verdad los datos personales, y no solo los
/// de la fila de <c>usuarios</c>. Lo usa <see cref="ServicioCuenta.EliminarAsync"/>.
///
/// <para>
/// Va en dos tiempos a propósito. <see cref="PrepararAsync"/> deja los cambios SIN guardar, para
/// que entren en la misma transacción de la baja (si algo falla, no queda nada a medias). Y
/// <see cref="BorrarArchivosAsync"/> corre DESPUÉS de que la base confirmó: al revés, una base que
/// fallara dejaría filas apuntando a archivos ya borrados; así, lo peor que puede pasar es un
/// archivo huérfano. Es la misma regla de API-26-quitar, API-72-quitar y de las fotos de trabajos.
/// </para>
///
/// <para>
/// Lo que NO se borra, a sabiendas: la fila del perfil (citas, reseñas y auditoría la referencian;
/// queda anonimizada y despublicada), las credenciales (quedan sin nombre ni entidad, con su
/// estado) y los servicios del catálogo. Ver <see cref="PerfilPrestador.BorrarDatosPersonales"/>.
/// </para>
/// </summary>
public sealed class ServicioBorradoDeDatos
{
    private readonly IRepositorioPerfiles _perfiles;
    private readonly IRepositorioPush _push;
    private readonly IServicioCredenciales _credenciales;
    private readonly IAlmacenDeArchivos _almacen;
    private readonly IAuditor _auditor;
    private readonly IRelojSistema _reloj;
    private readonly ILogger<ServicioBorradoDeDatos> _registro;

    public ServicioBorradoDeDatos(
        IRepositorioPerfiles perfiles, IRepositorioPush push, IServicioCredenciales credenciales,
        IAlmacenDeArchivos almacen, IAuditor auditor, IRelojSistema reloj,
        ILogger<ServicioBorradoDeDatos> registro)
    {
        _perfiles = perfiles;
        _push = push;
        _credenciales = credenciales;
        _almacen = almacen;
        _auditor = auditor;
        _reloj = reloj;
        _registro = registro;
    }

    /// <summary>
    /// Anonimiza la ficha, marca para borrar las fotos, los documentos y los navegadores con push, y
    /// devuelve las claves de los archivos a borrar cuando la base confirme. NO guarda.
    /// </summary>
    public async Task<IReadOnlyList<string>> PrepararAsync(
        Guid usuarioId, PerfilPrestador? perfil, CancellationToken ct = default)
    {
        var archivos = new List<string>();

        await _push.BorrarTodasDeUsuarioAsync(usuarioId, ct);

        if (perfil is not null)
        {
            // La foto de perfil son dos archivos: la grande y la chica (como en API-72-quitar).
            var foto = ServicioFotoDePerfil.ClaveDe(perfil.UrlFoto);
            if (foto is not null)
            {
                archivos.Add(foto);
                archivos.Add(TiposDeArchivo.ClaveChica(foto));
            }

            // Las fotos de trabajos, fila y archivo (como en API-73-borrar).
            var trabajos = await _perfiles.FotosDeTrabajoDePerfilAsync(perfil.Id, ct);
            foreach (var trabajo in trabajos)
            {
                archivos.Add(trabajo.ClaveAlmacen);
                _perfiles.EliminarFotoTrabajo(trabajo);
            }

            // Los documentos de credenciales: filas y archivos.
            var documentos = await _credenciales.PrepararBajaDeCuentaAsync(perfil.Id, ct);
            archivos.AddRange(documentos);

            perfil.BorrarDatosPersonales();
            perfil.ActualizadoEn = _reloj.Ahora;

            _auditor.Registrar(Auditar.Entrada(
                "baja_borrar_datos_perfil", "perfil_prestador", perfil.Id, _reloj.Ahora, actorId: usuarioId,
                contexto: new { fotosDeTrabajo = trabajos.Count, documentos = documentos.Count }));
        }

        return archivos;
    }

    /// <summary>
    /// Borra los archivos, ya con la base confirmada. Si uno falla, la baja NO se deshace: queda
    /// registrado como error (solo la clave, sin datos de la persona) para limpiarlo a mano.
    /// </summary>
    public async Task BorrarArchivosAsync(IEnumerable<string> claves)
    {
        foreach (var clave in claves)
        {
            try
            {
                var r = await _almacen.BorrarAsync(clave, CancellationToken.None);
                if (!r.Exito)
                    _registro.LogError(
                        "Baja de cuenta: no se pudo borrar el archivo {Clave} ({Motivo}). Queda huérfano.",
                        clave, r.Mensaje);
            }
            catch (Exception ex)
            {
                _registro.LogError(ex,
                    "Baja de cuenta: no se pudo borrar el archivo {Clave}. Queda huérfano.", clave);
            }
        }
    }
}
