using System.Text.RegularExpressions;
using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Catalogo;

/// <summary>
/// MOD-03 - Catalogo. TBL-07 rubro, TBL-08 servicio, TBL-09 prestador_servicio.
///
/// Modulo hoja: no llama a ningun otro modulo. El rubro del perfil (MOD-01) y el tope de
/// servicios del plan (MOD-13) llegan dentro de <see cref="ReemplazarServiciosCmd"/>.
/// </summary>
public sealed class ServicioCatalogo : IServicioCatalogo
{
    /// <summary>Cuantos rubros devuelve el autocompletado. Hoy existen 5; el tope es por si crecen.</summary>
    public const int MaxRubrosSugeridos = 20;

    /// <summary>
    /// Limites de la duracion de una atencion. Diez minutos es lo mas corto que tiene
    /// sentido agendar y ocho horas es una jornada: mas alla, lo que falla es el dato.
    /// La base los vuelve a comprobar con ck_prestador_servicio_duracion.
    /// </summary>
    public const short DuracionMinima = 10;
    public const short DuracionMaxima = 480;

    public const int LargoMinimoNombre = 3;
    public const int LargoMaximoNombre = 120;

    private readonly IRepositorioCatalogo _repo;
    private readonly IUnidadDeTrabajo _trabajo;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;

    public ServicioCatalogo(
        IRepositorioCatalogo repo, IUnidadDeTrabajo trabajo,
        IRelojSistema reloj, IAuditor auditor)
    {
        _repo = repo;
        _trabajo = trabajo;
        _reloj = reloj;
        _auditor = auditor;
    }

    // ------------------------------------------------------------- API-02

    public async Task<IReadOnlyList<RubroDto>> BuscarRubrosAsync(
        string? texto = null, CancellationToken ct = default)
    {
        var rubros = await _repo.RubrosActivosAsync(texto?.Trim(), MaxRubrosSugeridos, ct);
        return rubros.Select(r => new RubroDto(r.Id, r.Codigo, r.Nombre, r.Icono)).ToList();
    }

    public async Task<bool> ExisteRubroActivoAsync(short rubroId, CancellationToken ct = default) =>
        await _repo.RubroActivoPorIdAsync(rubroId, ct) is not null;

    public async Task<RubroDto?> RubroPorCodigoAsync(
        string codigo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return null;
        var rubro = await _repo.RubroActivoPorCodigoAsync(codigo, ct);
        return rubro is null ? null : new RubroDto(rubro.Id, rubro.Codigo, rubro.Nombre, rubro.Icono);
    }

    // ------------------------------------------------------------- API-03

    public async Task<Resultado<IReadOnlyList<ServicioDto>>> ServiciosDeRubroAsync(
        short rubroId, CancellationToken ct = default)
    {
        // Un rubro apagado se trata como inexistente: si no aparece en el selector,
        // pedir sus servicios por el identificador tampoco puede funcionar.
        var rubro = await _repo.RubroActivoPorIdAsync(rubroId, ct);
        if (rubro is null)
            return Resultado.Falla<IReadOnlyList<ServicioDto>>(
                Fallas.NoEncontrado, "Ese rubro no existe o ya no esta activo.");

        var servicios = await _repo.ServiciosDeCatalogoAsync(rubroId, ct);
        IReadOnlyList<ServicioDto> dto = servicios
            .Select(s => new ServicioDto(s.Id, s.Nombre, s.Descripcion, s.EsCatalogo))
            .ToList();

        return Resultado.Ok(dto);
    }

    // ------------------------------------------------------------- oferta del prestador

    public async Task<IReadOnlyList<ServicioOfrecidoDto>> ServiciosDePrestadorAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var oferta = await _repo.OfertaDePrestadorAsync(perfilPrestadorId, ct);
        return oferta
            .Select(o => new ServicioOfrecidoDto(
                o.Id,
                o.Servicio?.Id ?? o.ServicioId,
                o.Servicio?.Nombre ?? "",
                o.Servicio?.EsCatalogo ?? true,
                o.DuracionMinutos,
                o.ModoAgenda.ToString(),
                o.PrecioReferencia,
                o.Activo))
            .OrderBy(o => o.Nombre)
            .ToList();
    }

    /// <summary>
    /// API-20. Todo se valida antes de tocar una sola fila: una peticion con un error en
    /// el ultimo servicio no puede dejar guardados los primeros.
    /// </summary>
    public async Task<Resultado> ReemplazarServiciosAsync(
        ReemplazarServiciosCmd cmd, CancellationToken ct = default)
    {
        // RN-S1-05 pide al menos un servicio para publicar. Vaciar la lista por API-20
        // dejaria un perfil "publicado" que no ve nadie: eso se hace despublicando.
        if (cmd.Servicios.Count == 0)
            return Resultado.Falla(Fallas.ReglaDeNegocio,
                "Tienes que ofrecer al menos un servicio. Si quieres dejar de aparecer, " +
                "despublica el perfil.");

        // Un tope en cero significa que no se pudo leer el plan, no que el plan
        // permita cero servicios. Decirle a la persona "tu plan permite hasta 0" la
        // manda a revisar su suscripcion cuando el problema esta en otro lado.
        if (cmd.MaxServicios <= 0)
            return Resultado.Falla(Fallas.ReglaDeNegocio,
                "No pudimos leer el tope de servicios de tu plan. Vuelve a intentarlo.");

        if (cmd.Servicios.Count > cmd.MaxServicios)
            return Resultado.Falla(Fallas.ReglaDeNegocio,
                $"Tu plan permite hasta {cmd.MaxServicios} servicios y estas enviando " +
                $"{cmd.Servicios.Count}.");

        var rubro = await _repo.RubroActivoPorIdAsync(cmd.RubroId, ct);
        if (rubro is null)
            return Resultado.Falla(Fallas.ReglaDeNegocio,
                "Elige primero el rubro de tu perfil: los servicios pertenecen a un rubro.");

        // ---- 1. Cada linea, por separado

        var pedidos = new List<Pedido>(cmd.Servicios.Count);
        foreach (var s in cmd.Servicios)
        {
            var tieneId = s.ServicioId is > 0;
            var tieneNombre = !string.IsNullOrWhiteSpace(s.NombreNuevo);

            // CU-03 paso 4 dice "selecciona o agrega", no las dos cosas. Aceptar ambas
            // obligaria a inventar cual gana, y la pantalla no tendria como saberlo.
            if (tieneId && tieneNombre)
                return Resultado.Falla(Fallas.Validacion,
                    "Cada servicio se elige del catalogo o se escribe nuevo, no las dos cosas.");
            if (!tieneId && !tieneNombre)
                return Resultado.Falla(Fallas.Validacion,
                    "Hay un servicio sin elegir del catalogo y sin nombre.");

            if (s.DuracionMinutos < DuracionMinima || s.DuracionMinutos > DuracionMaxima)
                return Resultado.Falla(Fallas.Validacion,
                    $"La duracion tiene que estar entre {DuracionMinima} y {DuracionMaxima} minutos.");

            if (!Enum.TryParse<ModoAgenda>(s.ModoAgenda, ignoreCase: true, out var modo))
                return Resultado.Falla(Fallas.Validacion,
                    "El modo de agenda tiene que ser automatico o manual.");

            if (s.PrecioReferencia is < 0)
                return Resultado.Falla(Fallas.Validacion,
                    "El precio de referencia no puede ser negativo.");

            string? nombre = null;
            if (tieneNombre)
            {
                nombre = Normalizar(s.NombreNuevo!);
                if (nombre.Length < LargoMinimoNombre || nombre.Length > LargoMaximoNombre)
                    return Resultado.Falla(Fallas.Validacion,
                        $"El nombre del servicio tiene que tener entre {LargoMinimoNombre} y " +
                        $"{LargoMaximoNombre} caracteres.");
            }

            pedidos.Add(new Pedido(
                s.ServicioId, nombre, nombre is null ? null : Texto.ClaveDeNombre(nombre),
                s.DuracionMinutos, modo, s.PrecioReferencia));
        }

        // ---- 2. Los del catalogo: existen, y son de este rubro

        var ids = pedidos.Where(p => p.ServicioId is > 0).Select(p => p.ServicioId!.Value).ToList();
        if (ids.Count != ids.Distinct().Count())
            return Resultado.Falla(Fallas.Validacion,
                "Hay un servicio repetido en la lista.");

        // UNA consulta para los N, no N consultas: esto corre en el guardado del perfil.
        var encontrados = ids.Count == 0
            ? Array.Empty<Servicio>()
            : (await _repo.ServiciosPorIdsAsync(ids, ct)).ToArray();

        foreach (var id in ids)
        {
            var servicio = encontrados.FirstOrDefault(s => s.Id == id);
            if (servicio is null)
                return Resultado.Falla(Fallas.Validacion, $"El servicio {id} no existe.");

            // Ofrecer un servicio de otro rubro dejaria al prestador apareciendo en
            // busquedas que no son la suya.
            if (servicio.RubroId != cmd.RubroId)
                return Resultado.Falla(Fallas.Validacion,
                    $"El servicio \"{servicio.Nombre}\" es de otro rubro.");
        }

        // ---- 3. Los nuevos: o ya existia uno con ese nombre, o hay que crearlo

        var claves = pedidos.Where(p => p.Clave is not null).Select(p => p.Clave!).ToList();
        if (claves.Distinct().Count() != claves.Count)
            return Resultado.Falla(Fallas.Validacion,
                "Hay un servicio repetido en la lista.");

        // Resolver primero y crear despues: mientras se resuelve todavia puede aparecer
        // un error, y nada debe haberse insertado cuando eso pase.
        var porCrear = new List<Pedido>();
        for (var i = 0; i < pedidos.Count; i++)
        {
            var p = pedidos[i];
            if (p.Nombre is null) continue;

            // "Tasacion " escrito a mano no puede crear un segundo servicio al lado del
            // que ya esta en el catalogo: quedarian dos filas para lo mismo.
            var existente = await _repo.ServicioPorNombreAsync(cmd.RubroId, p.Clave!, ct);
            if (existente is not null)
            {
                if (ids.Contains(existente.Id))
                    return Resultado.Falla(Fallas.Validacion,
                        $"Ya estas ofreciendo \"{existente.Nombre}\": esta dos veces en la lista.");

                ids.Add(existente.Id);
                pedidos[i] = p with { ServicioId = existente.Id, Nombre = null, Clave = null };
            }
            else
            {
                porCrear.Add(p);
            }
        }

        // ---- 4. Recien ahora se escribe

        var creados = new Dictionary<string, Servicio>();
        foreach (var p in porCrear)
        {
            var nuevo = new Servicio
            {
                RubroId = cmd.RubroId,
                Nombre = p.Nombre!,
                NombreNormalizado = p.Clave!,
                EsCatalogo = false,   // TBL-08: falso si lo agrego un prestador
            };
            _repo.AgregarServicio(nuevo);
            creados[p.Clave!] = nuevo;
        }

        var actual = await _repo.OfertaDePrestadorAsync(cmd.PerfilPrestadorId, ct);

        foreach (var p in pedidos)
        {
            if (p.ServicioId is > 0)
            {
                var ya = actual.FirstOrDefault(o => o.ServicioId == p.ServicioId!.Value);
                if (ya is not null)
                {
                    // Se actualiza la fila, no se recrea: TBL-18 cita y TBL-13
                    // disponibilidad apuntan aqui con borrado en cascada.
                    ya.DuracionMinutos = p.Duracion;
                    ya.ModoAgenda = p.Modo;
                    ya.PrecioReferencia = p.Precio;
                    ya.Activo = true;
                    continue;
                }

                _repo.AgregarOferta(new PrestadorServicio
                {
                    PerfilPrestadorId = cmd.PerfilPrestadorId,
                    ServicioId = p.ServicioId!.Value,
                    DuracionMinutos = p.Duracion,
                    ModoAgenda = p.Modo,
                    PrecioReferencia = p.Precio,
                    Activo = true,
                });
                continue;
            }

            // Servicio recien creado: su identificador lo asigna la base al guardar, asi
            // que se enlaza por la navegacion y EF Core rellena la clave ajena.
            var servicioNuevo = creados[p.Clave!];
            _repo.AgregarOferta(new PrestadorServicio
            {
                PerfilPrestadorId = cmd.PerfilPrestadorId,
                Servicio = servicioNuevo,
                ServicioId = servicioNuevo.Id,
                DuracionMinutos = p.Duracion,
                ModoAgenda = p.Modo,
                PrecioReferencia = p.Precio,
                Activo = true,
            });
        }

        // Lo que salio de la lista se desactiva y se queda. Borrarlo arrastraria en
        // cascada las citas y los horarios de ese servicio.
        foreach (var sobrante in actual.Where(o => !ids.Contains(o.ServicioId)))
            sobrante.Activo = false;

        _auditor.Registrar(Auditar.Entrada(
            "reemplazar_servicios", "perfil_prestador", cmd.PerfilPrestadorId, _reloj.Ahora,
            contexto: new { cuantos = cmd.Servicios.Count, nuevos = porCrear.Count }));

        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------- MOD-06

    public async Task<Resultado<DatosOfertaDto>> DatosDePrestadorServicioAsync(
        Guid prestadorServicioId, CancellationToken ct = default)
    {
        var oferta = await _repo.OfertaPorIdAsync(prestadorServicioId, ct);
        if (oferta is null)
            return Resultado.Falla<DatosOfertaDto>(
                Fallas.NoEncontrado, "Ese servicio del prestador no existe.");

        return Resultado.Ok(new DatosOfertaDto(
            oferta.Id,
            oferta.PerfilPrestadorId,
            oferta.ServicioId,
            oferta.Servicio?.Nombre ?? "",
            oferta.DuracionMinutos,
            oferta.ModoAgenda.ToString(),
            oferta.Activo));
    }

    // ------------------------------------------------------------- auxiliares

    /// <summary>
    /// El nombre tal como se va a mostrar: sin espacios de sobra, sin saltos de linea y
    /// sin caracteres de control, pero conservando tildes y mayusculas como se escribio.
    /// Lo que se compara para no duplicar es Texto.ClaveDeNombre, no esto.
    /// </summary>
    private static string Normalizar(string texto)
    {
        var limpio = new string(texto.Where(c => !char.IsControl(c)).ToArray());
        return Regex.Replace(limpio, @"\s+", " ").Trim();
    }

    private sealed record Pedido(
        int? ServicioId, string? Nombre, string? Clave,
        short Duracion, ModoAgenda Modo, int? Precio);
}
