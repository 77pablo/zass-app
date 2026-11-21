using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Comun;
using Zass.Dominio.Tarjetas;

namespace Zass.Aplicacion.Tarjetas;

/// <summary>
/// MOD-12 - S6. CS-18 (API-54), API-55 (Activar), API-56 a API-58 (CU-20), API-59 (Emitir).
/// </summary>
public sealed class ServicioTarjetas : IServicioTarjetas
{
    /// <summary>CU-19 4B.5: numero de intentos fallidos en la ventana para bloquear la tarjeta.</summary>
    public const int IntentosParaBloquear = 5;

    /// <summary>CU-19 4B.5: ventana de tiempo (15 minutos) para contar intentos fallidos.</summary>
    public static readonly TimeSpan VentanaDeIntentos = TimeSpan.FromMinutes(15);

    /// <summary>CU-19 4B.5: duracion del bloqueo (1 hora) tras alcanzar el limite.</summary>
    public static readonly TimeSpan DuracionDelBloqueo = TimeSpan.FromHours(1);

    /// <summary>
    /// La forma de <see cref="Tarjeta.GenerarUrlEstable"/>: 4+4 del alfabeto sin I, L, O, 0 ni 1.
    /// Lo que no calza ni llega a la base.
    /// </summary>
    private static readonly Regex FormaDeUrlEstable = new(
        "^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$", RegexOptions.CultureInvariant);

    /// <summary>Formato del codigo de activacion: 3 grupos de 4 separados por guion.</summary>
    private static readonly Regex FormaDeCodigo = new(
        "^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$", RegexOptions.CultureInvariant);

    private static readonly EscaneoResuelto NoValida = new(DestinoTarjeta.NoValida, null);

    private readonly IRepositorioTarjetas _repo;
    private readonly IUnidadDeTrabajo _trabajo;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;
    private readonly IGeneradorSecretos _secretos;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IAlertasAdministrador _alertas;

    public ServicioTarjetas(
        IRepositorioTarjetas repo,
        IUnidadDeTrabajo trabajo,
        IRelojSistema reloj,
        IAuditor auditor,
        IGeneradorSecretos secretos,
        IRepositorioAuditoria auditoria,
        IAlertasAdministrador alertas)
    {
        _repo = repo;
        _trabajo = trabajo;
        _reloj = reloj;
        _auditor = auditor;
        _secretos = secretos;
        _auditoria = auditoria;
        _alertas = alertas;
    }

    public async Task<EscaneoResuelto> RegistrarEscaneoAsync(
        string? segmento, MedioEscaneo medio, CancellationToken ct = default)
    {
        // Impresa va en mayusculas, pero hay lectores de QR que la bajan a minusculas.
        var urlEstable = (segmento ?? "").Trim().ToUpperInvariant();
        if (urlEstable.Length != 9 || !FormaDeUrlEstable.IsMatch(urlEstable)) return NoValida;

        var tarjeta = await _repo.PorUrlEstableAsync(urlEstable, ct);
        if (tarjeta is null) return NoValida;

        var destino = tarjeta.Resolver();
        // Solo cuenta lo que llega a una ficha: el prestador ve sus escaneos (API-58), y una
        // tarjeta muerta o sin activar no es de nadie que deba verlos. Ademas, asi una tarjeta
        // desactivada y una inexistente hacen exactamente el mismo trabajo.
        if (destino != DestinoTarjeta.Ficha) return new EscaneoResuelto(destino, null);

        _repo.AgregarEscaneo(new EscaneoTarjeta
        {
            TarjetaId = tarjeta.Id,
            Medio = Enum.IsDefined(medio) ? medio : MedioEscaneo.Nfc,
            OcurridoEn = _reloj.Ahora,
        });
        await _trabajo.GuardarAsync(ct);

        return new EscaneoResuelto(DestinoTarjeta.Ficha, tarjeta.PerfilPrestador!.Slug);
    }

    // ------------------------------------------------------------- API-56 a API-58 (CU-20)
    // Ninguno mira la suscripcion: listar y desactivar se permiten siempre (perder una tarjeta
    // no puede quedar sin salida porque el plan vencio).

    /// <summary>Ajena e inexistente dicen exactamente lo mismo (403 y 404 responden igual).</summary>
    private static readonly Resultado NoEncontrada =
        Resultado.Falla(Fallas.NoEncontrado, "Esa tarjeta no existe.");

    public async Task<IReadOnlyList<MiTarjetaDto>> MisTarjetasAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var tarjetas = await _repo.DePrestadorAsync(perfilPrestadorId, ct);
        return tarjetas
            .Select(t => new MiTarjetaDto(
                t.Id, t.UrlEstable, EstadoVisible(t.Estado), t.ActivadaEn, t.DesactivadaEn))
            .ToList();
    }

    public async Task<Resultado> DesactivarAsync(
        Guid perfilPrestadorId, Guid tarjetaId, CancellationToken ct = default)
    {
        var tarjeta = await PropiaAsync(perfilPrestadorId, tarjetaId, ct);
        if (tarjeta is null) return NoEncontrada;

        // Ya muerta: no se toca, para que la fecha de baja siga siendo la primera.
        if (tarjeta.Estado != EstadoTarjeta.Activa) return Resultado.Ok();

        tarjeta.Desactivar(_reloj.Ahora);
        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok();
    }

    public async Task<Resultado<EstadisticasDeTarjetaDto>> EstadisticasAsync(
        Guid perfilPrestadorId, Guid tarjetaId, CancellationToken ct = default)
    {
        var tarjeta = await PropiaAsync(perfilPrestadorId, tarjetaId, ct);
        if (tarjeta is null)
            return Resultado.Falla<EstadisticasDeTarjetaDto>(NoEncontrada.Codigo!, NoEncontrada.Mensaje!);

        var ahora = _reloj.Ahora;
        var c = await _repo.EscaneosPorMedioAsync(tarjeta.Id, ahora.AddDays(-7), ahora.AddDays(-30), ct);
        return Resultado.Ok(new EstadisticasDeTarjetaDto(
            new ConteoPorMedio(c.Nfc7, c.Qr7),
            new ConteoPorMedio(c.Nfc30, c.Qr30),
            new ConteoPorMedio(c.NfcTotal, c.QrTotal)));
    }

    /// <summary>
    /// La comprobacion de dueno vive AQUI y no en la consulta: el repositorio busca por id, y
    /// si este filtro se cae, la prueba unitaria lo ve.
    /// </summary>
    private async Task<Tarjeta?> PropiaAsync(Guid perfilPrestadorId, Guid tarjetaId, CancellationToken ct)
    {
        var tarjeta = await _repo.PorIdAsync(tarjetaId, ct);
        return tarjeta is not null && tarjeta.PerfilPrestadorId == perfilPrestadorId ? tarjeta : null;
    }

    /// <summary>Sin reemplazo todavia (pendiente): para el dueno, una reemplazada es una desactivada.</summary>
    private static string EstadoVisible(EstadoTarjeta estado) =>
        estado == EstadoTarjeta.Reemplazada ? nameof(EstadoTarjeta.Desactivada) : estado.ToString();

    // ------------------------------------------------------------- API-59 (CU-19, CU-20)
    /// <summary>
    /// API-59 POST /api/interno/tarjetas. Emite una tarjeta nueva con su codigo de activacion.
    /// Devuelve el codigo EN CLARO una sola vez (201). Nunca se puede volver a leer.
    /// Tasa S (Sensible). Requiere rol Administrador.
    /// </summary>
    public async Task<Resultado<EmitirTarjetaResultado>> EmitirAsync(CancellationToken ct = default)
    {
        var codigo = Tarjeta.GenerarCodigo();
        var hash = _secretos.HashToken(codigo);
        var venceEn = _reloj.Ahora.AddDays(30);

        var tarjeta = new Tarjeta
        {
            UrlEstable = Tarjeta.GenerarUrlEstable(),
            HashCodigoActivacion = hash,
            CodigoVenceEn = venceEn,
            Estado = EstadoTarjeta.Emitida,
            EmitidaEn = _reloj.Ahora,
        };

        _repo.Agregar(tarjeta);

        // Auditoria SIN el codigo en claro (antes de GuardarAsync para que se guarde en la misma transaccion)
        _auditor.Registrar(new RegistroAuditoria
        {
            Accion = "TarjetaEmitida",
            Entidad = "Tarjeta",
            EntidadId = tarjeta.Id,
            Contexto = JsonSerializer.Serialize(new { UrlEstable = tarjeta.UrlEstable, VenceEn = venceEn }),
            OcurridoEn = _reloj.Ahora,
        });

        await _trabajo.GuardarAsync(ct);

        return Resultado.Ok(new EmitirTarjetaResultado(tarjeta.UrlEstable, codigo, venceEn));
    }

    // ------------------------------------------------------------- API-55 (CU-19)
    /// <summary>
    /// API-55 POST /t/{segmento}/activar. Activa la tarjeta con codigo de un solo uso.
    /// Implementa CC-05: consumo atomico en la misma transaccion con bloqueo de fila.
    /// Implementa 4B.5: bloqueo tras {IntentosParaBloquear} intentos fallidos en {VentanaDeIntentos.TotalMinutes} minutos ({DuracionDelBloqueo.TotalHours} hora).
    /// Tasa A (Autenticacion). Requiere sesion prestador con perfil publicado.
    /// </summary>
    public async Task<Resultado<ActivarTarjetaResultado>> ActivarAsync(
        string segmento, string codigo, Guid perfilPrestadorId, string? ipOrigen, CancellationToken ct = default)
    {
        // Validar formato del codigo antes de ir a la base
        var codigoNorm = (codigo ?? "").Trim().ToUpperInvariant();
        if (!FormaDeCodigo.IsMatch(codigoNorm))
            return Resultado.Falla<ActivarTarjetaResultado>(Fallas.ReglaDeNegocio, "El código tiene 12 caracteres.");

        var urlEstable = (segmento ?? "").Trim().ToUpperInvariant();
        if (urlEstable.Length != 9 || !FormaDeUrlEstable.IsMatch(urlEstable))
            return Resultado.Falla<ActivarTarjetaResultado>(Fallas.NoEncontrado, "Tarjeta no válida.");

        // Ejecutar la transaccion y devolver si hay que alertar
        var (resultado, alerta) = await TransaccionDeActivarAsync(urlEstable, codigoNorm, perfilPrestadorId, ipOrigen, ct);

        // FUERA de la transaccion: enviar la alerta si corresponde (solo una vez por bloqueo)
        if (alerta is not null)
        {
            await _alertas.AvisarTarjetaBloqueadaAsync(alerta, ct);
        }

        return resultado;
    }

    /// <summary>
    /// Ejecuta la transaccion de activacion y devuelve el resultado y si hay que alertar.
    /// La alerta se envia FUERA de la transaccion (despues del commit).
    /// </summary>
    private async Task<(Resultado<ActivarTarjetaResultado> Resultado, AlertaTarjetaBloqueada? Alerta)> TransaccionDeActivarAsync(
        string urlEstable, string codigoNorm, Guid perfilPrestadorId, string? ipOrigen, CancellationToken ct)
    {
        return await _trabajo.EnTransaccionAsync(async ct =>
        {
            // Tomar la fila CON BLOQUEO (CC-05) - solo si esta en estado Emitida
            var tarjeta = await _repo.TomarParaActivarAsync(urlEstable, ct);
            if (tarjeta is null)
            {
                // Distinguir "no existe" de "ya no esta en Emitida (ya activada/vencida)"
                // para no revelar informacion: mismo error que codigo incorrecto.
                var existe = await _repo.ExistePorUrlEstableAsync(urlEstable, ct);
                if (existe)
                {
                    // La tarjeta existe pero ya no esta en Emitida (ya activada, vencida, etc.)
                    // Mismo mensaje que codigo incorrecto (no revelar cual)
                    return (Resultado.Falla<ActivarTarjetaResultado>(Fallas.ReglaDeNegocio, "El código no es válido."), default(AlertaTarjetaBloqueada?));
                }
                // Tarjeta no existe
                return (Resultado.Falla<ActivarTarjetaResultado>(Fallas.NoEncontrado, "Tarjeta no válida."), default(AlertaTarjetaBloqueada?));
            }

            // 4B.5: Verificar si la tarjeta esta bloqueada ANTES de validar el codigo
            // Bloqueada = en los ultimos 60 min hay un intento fallido F tal que entre F-15min y F hay 5+ intentos
            var (estaBloqueada, finBloqueo) = await EstaBloqueadaAsync(tarjeta.Id, ct);
            if (estaBloqueada)
            {
                // Mientras este bloqueada, cualquier intento (incluso con codigo CORRECTO)
                // responde IGUAL que un codigo incorrecto y no consume el codigo
                return (Resultado.Falla<ActivarTarjetaResultado>(Fallas.ReglaDeNegocio, "El código no es válido."), default(AlertaTarjetaBloqueada?));
            }

            // Verificar vigencia del codigo (30 dias)
            var codigoVencido = tarjeta.CodigoVenceEn.HasValue && _reloj.Ahora > tarjeta.CodigoVenceEn.Value;

            // Verificar hash con tiempo constante
            var hashRecibido = _secretos.HashToken(codigoNorm);
            var codigoIncorrecto = !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(hashRecibido),
                System.Text.Encoding.UTF8.GetBytes(tarjeta.HashCodigoActivacion!));

            if (codigoVencido || codigoIncorrecto)
            {
                // Registrar intento fallido en auditoria (SIN el codigo ni su hash en el contexto)
                _auditor.Registrar(new RegistroAuditoria
                {
                    ActorId = perfilPrestadorId,
                    Accion = "TarjetaActivacionFallida",
                    Entidad = "Tarjeta",
                    EntidadId = tarjeta.Id,
                    Contexto = JsonSerializer.Serialize(new { UrlEstable = tarjeta.UrlEstable, Vencido = codigoVencido }),
                    IpOrigen = ipOrigen,
                    OcurridoEn = _reloj.Ahora,
                });

                // Guardar PRIMERO para que el fallo este en la base antes de contar (D1)
                await _trabajo.GuardarAsync(ct);

                // Verificar si este fallo es el 5.o en una ventana de 15 min -> bloqueo y aviso a admin
                // Contar los fallos en los ultimos 15 minutos (incluyendo el actual que ya se guardo)
                var ahora = _reloj.Ahora;
                var ventana15Min = ahora - VentanaDeIntentos;
                var cantidadFallos = await _auditoria.ContarActivacionesFallidasAsync(tarjeta.Id, ventana15Min, ahora, ct);
                
                AlertaTarjetaBloqueada? alerta = null;
                if (cantidadFallos >= IntentosParaBloquear)
                {
                    // Preparar la alerta (se envia FUERA de la transaccion)
                    alerta = new AlertaTarjetaBloqueada(tarjeta.UrlEstable, ahora, ahora + DuracionDelBloqueo);

                    // Registrar el aviso de bloqueo en auditoria (dentro de la transaccion)
                    _auditor.Registrar(new RegistroAuditoria
                    {
                        ActorId = null, // Sistema
                        Accion = "TarjetaBloqueadaPorIntentos",
                        Entidad = "Tarjeta",
                        EntidadId = tarjeta.Id,
                        Contexto = JsonSerializer.Serialize(new
                        {
                            Motivo = $"{IntentosParaBloquear} intentos fallidos en {VentanaDeIntentos.TotalMinutes} minutos",
                            BloqueadaHasta = ahora + DuracionDelBloqueo,
                            UrlEstable = tarjeta.UrlEstable
                        }),
                        IpOrigen = null,
                        OcurridoEn = ahora,
                    });

                    // Guardar el aviso de bloqueo en la misma transaccion
                    await _trabajo.GuardarAsync(ct);
                }

                // MISMO mensaje que codigo incorrecto (no revelar cual)
                return (Resultado.Falla<ActivarTarjetaResultado>(Fallas.ReglaDeNegocio, "El código no es válido."), alerta);
            }

            // Codigo correcto y no vencido: consumir y activar
            tarjeta.PerfilPrestadorId = perfilPrestadorId;
            tarjeta.Estado = EstadoTarjeta.Activa;
            tarjeta.ActivadaEn = _reloj.Ahora;
            tarjeta.HashCodigoActivacion = null; // Consumo atomico - borrar el hash

            // Auditoria (antes de GuardarAsync para que se guarde en la misma transaccion)
            _auditor.Registrar(new RegistroAuditoria
            {
                ActorId = perfilPrestadorId,
                Accion = "TarjetaActivada",
                Entidad = "Tarjeta",
                EntidadId = tarjeta.Id,
                Contexto = JsonSerializer.Serialize(new { UrlEstable = tarjeta.UrlEstable }),
                IpOrigen = ipOrigen,
                OcurridoEn = _reloj.Ahora,
            });

            await _trabajo.GuardarAsync(ct);

            // Notificacion (A-08) - pendiente en otra tarea
            // var slug = tarjeta.PerfilPrestador?.Slug; // necesitamos el slug

            return (Resultado.Ok(new ActivarTarjetaResultado("Tarjeta activada", $"/prestadores/{tarjeta.PerfilPrestador!.Slug}")), default(AlertaTarjetaBloqueada?));
        }, ct);
    }

    /// <summary>
    /// Verifica si la tarjeta esta actualmente bloqueada por 4B.5.
    /// Bloqueada = en los ultimos {DuracionDelBloqueo.TotalMinutes} minutos hay un intento fallido F tal que
    /// entre F-{VentanaDeIntentos.TotalMinutes}min y F hay {IntentosParaBloquear} o mas intentos fallidos de ESA tarjeta.
    /// El bloqueo dura {DuracionDelBloqueo.TotalHours} hora desde ese intento F (el {IntentosParaBloquear}.o fallo).
    /// D4: Esta funcion reemplaza a las antiguas EstaBloqueadaAsync y EsQuintoFalloEnVentanaAsync.
    /// D2: Usa ObtenerFechasDeFallosAsync con filtro en SQL en vez de ConsultarAsync + filtro en memoria.
    /// </summary>
    private async Task<(bool Bloqueada, DateTime? FinBloqueo)> EstaBloqueadaAsync(Guid tarjetaId, CancellationToken ct)
    {
        var ahora = _reloj.Ahora;
        var ventana60Min = ahora - DuracionDelBloqueo;

        // Contar fallos en los ultimos 60 minutos usando la consulta SQL especializada (D2)
        var totalFallos60Min = await _auditoria.ContarActivacionesFallidasAsync(tarjetaId, ventana60Min, ahora, ct);
        if (totalFallos60Min < IntentosParaBloquear) return (false, null);

        // Obtener solo las fechas de los fallos (filtro en SQL, D2)
        var fechasDeFallos = await _auditoria.ObtenerFechasDeFallosAsync(tarjetaId, ventana60Min, ahora, ct);

        if (fechasDeFallos.Count < IntentosParaBloquear) return (false, null);

        // Para cada fallo F, contar cuantos hay en [F-15min, F]
        for (int i = 0; i < fechasDeFallos.Count; i++)
        {
            var fallo = fechasDeFallos[i];
            var inicioVentana = fallo - VentanaDeIntentos;
            
            // Contar cuantos fallos hay en [F-15min, F]
            // Como la lista ya viene ordenada, podemos contar desde el inicio hasta i
            var cuentaEnVentana = fechasDeFallos.Take(i + 1).Count(f => f >= inicioVentana && f <= fallo);

            if (cuentaEnVentana >= IntentosParaBloquear)
            {
                // Este fallo F es el que disparó el bloqueo (el 5.o en su ventana de 15 min)
                var finBloqueo = fallo + DuracionDelBloqueo;
                if (ahora < finBloqueo)
                {
                    return (true, finBloqueo);
                }
                // Si ya pasó la hora, este bloqueo específico ya no aplica
                // (pero podría haber otro fallo más reciente que sí bloquee)
            }
        }

        return (false, null);
    }
}
