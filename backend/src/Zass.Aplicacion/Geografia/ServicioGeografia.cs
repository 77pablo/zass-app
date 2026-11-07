using Zass.Aplicacion.Comun;
using Zass.Aplicacion.Puertos;
using Zass.Dominio.Catalogo;
using Zass.Dominio.Comun;

namespace Zass.Aplicacion.Geografia;

/// <summary>
/// MOD-04 - Geolocalizacion. TBL-10 zona y TBL-11 cobertura_prestador.
///
/// Modulo hoja hacia dentro: no consume ningun otro modulo. Hacia fuera consume N-08, el
/// servicio de mapas, y lo hace a traves de <see cref="IGeocodificador"/> para que su
/// ausencia sea un caso previsto y no una caida.
/// </summary>
public sealed class ServicioGeografia : IServicioGeografia
{
    /// <summary>
    /// Hasta donde puede estar un punto del centroide mas cercano para que la zona
    /// signifique algo. Son los mismos 50 km del radio maximo de busqueda (RN-S3-04): si
    /// el producto no busca mas alla, tampoco tiene por que ubicar mas alla.
    /// </summary>
    public const int MaxKmDesdeElCentro = 50;

    /// <summary>
    /// Cuantas zonas puede cubrir un prestador.
    ///
    /// **Decision tomada aqui, no en el documento.** RN-S3-08 dice que puede cubrir
    /// varias y no pone numero. Sin tope, un prestador se marca las 30 zonas y sale en
    /// todas las busquedas de la ciudad, que es justo lo que un buscador llamaria
    /// publicidad. Diez cubre una ciudad del tamano de Temuco de sobra.
    /// </summary>
    public const int MaxZonas = 10;

    private readonly IRepositorioGeografia _repo;
    private readonly IGeocodificador _geocodificador;
    private readonly IUnidadDeTrabajo _trabajo;
    private readonly IRelojSistema _reloj;
    private readonly IAuditor _auditor;

    public ServicioGeografia(
        IRepositorioGeografia repo, IGeocodificador geocodificador,
        IUnidadDeTrabajo trabajo, IRelojSistema reloj, IAuditor auditor)
    {
        _repo = repo;
        _geocodificador = geocodificador;
        _trabajo = trabajo;
        _reloj = reloj;
        _auditor = auditor;
    }

    // ------------------------------------------------------------- API-04

    public async Task<IReadOnlyList<ZonaDto>> ZonasAsync(
        string? sector = null, CancellationToken ct = default)
    {
        var zonas = await _repo.ZonasAsync(
            string.IsNullOrWhiteSpace(sector) ? null : sector.Trim(), ct);
        return zonas.Select(Describir).ToList();
    }

    // ------------------------------------------------------------- CS-10

    public async Task<Resultado<ZonaDto>> ResolverZonaAsync(
        Punto punto, CancellationToken ct = default)
    {
        // Esto solo atrapa el fuera de rango. El punto 0,0 —lo que manda un formulario
        // vacio— pasa por aqui, porque 0 es una latitud perfectamente valida: a ese lo
        // rechaza el tope de 50 km de mas abajo, que cae en el golfo de Guinea. Lo
        // descubrio una mutacion: al quitar el tope, la prueba del 0,0 se cayo tambien.
        if (!punto.EsValido)
            return Resultado.Falla<ZonaDto>(Fallas.Validacion,
                "Esas coordenadas no son validas.");

        var zonas = await _repo.ZonasAsync(null, ct);
        if (zonas.Count == 0)
            return Resultado.Falla<ZonaDto>(Fallas.ReglaDeNegocio,
                "Todavia no hay zonas cargadas en el sistema.");

        var masCercana = zonas
            .Select(z => (zona: z, metros: Geo.DistanciaEnMetros(punto, Centro(z))))
            .OrderBy(x => x.metros)
            .First();

        if (masCercana.metros > MaxKmDesdeElCentro * 1000.0)
            return Resultado.Falla<ZonaDto>(Fallas.NoEncontrado,
                $"Ese punto queda a mas de {MaxKmDesdeElCentro} km de cualquier zona "
                + "que cubramos todavia.");

        return Resultado.Ok(Describir(masCercana.zona));
    }

    public async Task<Resultado<ZonaDto>> ZonaDeDireccionAsync(
        string direccion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(direccion))
            return Resultado.Falla<ZonaDto>(Fallas.Validacion, "Falta la direccion.");

        // RN-S3-06: esto se hace UNA vez, al guardar la direccion, nunca en cada
        // busqueda. Quien llame desde la busqueda esta usando el metodo equivocado.
        var punto = await _geocodificador.GeocodificarAsync(direccion.Trim(), ct);
        if (!punto.Exito)
            return Resultado.Falla<ZonaDto>(punto.Codigo!, punto.Mensaje!);

        return await ResolverZonaAsync(punto.Valor, ct);
    }

    // ------------------------------------------------------------- CU-10 / API-21

    public async Task<IReadOnlyList<ZonaCubiertaDto>> ZonasDeCoberturaAsync(
        Guid perfilPrestadorId, CancellationToken ct = default)
    {
        var cobertura = await _repo.CoberturaDePrestadorAsync(perfilPrestadorId, ct);
        return cobertura
            .Select(c => new ZonaCubiertaDto(
                c.ZonaId,
                c.Zona?.Nombre ?? "",
                c.Zona?.Sector ?? "",
                c.EsPrincipal,
                c.DesdeEn))
            // La principal primero: es la que se muestra en la ficha (RN-S3-08).
            .OrderByDescending(z => z.EsPrincipal)
            .ThenBy(z => z.Nombre)
            .ToList();
    }

    public async Task<Resultado> ReemplazarCoberturaAsync(
        ReemplazarCoberturaCmd cmd, CancellationToken ct = default)
    {
        // RN-S1-05 exige al menos una zona para publicar. Vaciar la lista por API-21
        // apagaria un perfil publicado sin despublicarlo.
        if (cmd.Zonas.Count == 0)
            return Resultado.Falla(Fallas.ReglaDeNegocio,
                "Tienes que cubrir al menos una zona. Si quieres dejar de aparecer, "
                + "despublica el perfil.");

        if (cmd.Zonas.Count > MaxZonas)
            return Resultado.Falla(Fallas.ReglaDeNegocio,
                $"Puedes cubrir hasta {MaxZonas} zonas y estas enviando {cmd.Zonas.Count}.");

        var ids = cmd.Zonas.Select(z => z.ZonaId).ToList();
        if (ids.Count != ids.Distinct().Count())
            return Resultado.Falla(Fallas.Validacion, "Hay una zona repetida en la lista.");

        // RN-S3-08 y R-2. Cero principales deja la ficha sin zona que mostrar; dos es lo
        // que ux_cobertura_principal rechaza con un 23505, y eso llegaria como un 500.
        var principales = cmd.Zonas.Count(z => z.EsPrincipal);
        if (principales == 0)
            return Resultado.Falla(Fallas.Validacion,
                "Marca cual es tu zona principal: es la que se muestra en tu ficha.");
        if (principales > 1)
            return Resultado.Falla(Fallas.Validacion,
                "Solo puede haber una zona principal.");

        // UNA consulta para las N, no una por zona.
        var encontradas = await _repo.ZonasPorIdsAsync(ids, ct);
        var faltante = ids.FirstOrDefault(id => encontradas.All(z => z.Id != id));
        if (faltante != 0)
            return Resultado.Falla(Fallas.Validacion, $"La zona {faltante} no existe.");

        var actual = await _repo.CoberturaDePrestadorAsync(cmd.PerfilPrestadorId, ct);

        // Paso 1: dejar a TODAS sin principal, y recien despues marcar la nueva.
        //
        // No es un rodeo, y esto esta MEDIDO, no supuesto: se escribio la version
        // ingenua —marcar la nueva en el mismo guardado— y la prueba
        // Cambiar_la_zona_principal_no_rompe_el_unico_de_la_base se cae contra
        // PostgreSQL. ux_cobertura_principal es un unico parcial sobre
        // (perfil_prestador_id) WHERE es_principal, y la base lo comprueba despues de
        // CADA sentencia, no al final de la transaccion, asi que cambiar de zona
        // principal termina en un 23505 que llegaria al usuario como un 500.
        //
        // En memoria las dos versiones pasan. Por eso la prueba vive en integracion.
        foreach (var c in actual) c.EsPrincipal = false;

        foreach (var quitada in actual.Where(c => !ids.Contains(c.ZonaId)).ToList())
            _repo.QuitarCobertura(quitada);

        foreach (var pedida in cmd.Zonas)
        {
            var ya = actual.FirstOrDefault(c => c.ZonaId == pedida.ZonaId);
            if (ya is null)
            {
                _repo.AgregarCobertura(new CoberturaPrestador
                {
                    PerfilPrestadorId = cmd.PerfilPrestadorId,
                    ZonaId = pedida.ZonaId,
                    EsPrincipal = false,       // se marca en el paso 2
                    DesdeEn = _reloj.Ahora,
                });
            }
        }

        await _trabajo.GuardarAsync(ct);

        // Paso 2: ahora si, y ya no hay ninguna principal con la que chocar.
        var todas = await _repo.CoberturaDePrestadorAsync(cmd.PerfilPrestadorId, ct);
        var principal = cmd.Zonas.Single(z => z.EsPrincipal).ZonaId;
        var fila = todas.FirstOrDefault(c => c.ZonaId == principal);
        if (fila is null)
            return Resultado.Falla(Fallas.Conflicto,
                "No se pudo marcar la zona principal. Vuelve a intentarlo.");
        fila.EsPrincipal = true;

        _auditor.Registrar(Auditar.Entrada(
            "reemplazar_cobertura", "perfil_prestador", cmd.PerfilPrestadorId, _reloj.Ahora,
            contexto: new { zonas = cmd.Zonas.Count, principal }));

        await _trabajo.GuardarAsync(ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------- distancias

    // Delegan en el dominio a proposito: la cuenta no depende de nada de esta capa, y
    // asi se puede probar sola. Estan aqui porque la tabla de modulos las declara como
    // operaciones de MOD-04, que es quien manda sobre lo geografico.
    public double DistanciaEnMetros(Punto a, Punto b) => Geo.DistanciaEnMetros(a, b);

    public string DistanciaPublica(Punto a, Punto b) =>
        Geo.DistanciaPublica(Geo.DistanciaEnMetros(a, b));

    // ------------------------------------------------------------- auxiliares

    private static Punto Centro(Zona z) => new(z.CentroLatitud, z.CentroLongitud);

    private static ZonaDto Describir(Zona z) => new(
        z.Id, z.Nombre, z.Sector, z.Comuna, z.CentroLatitud, z.CentroLongitud);
}
