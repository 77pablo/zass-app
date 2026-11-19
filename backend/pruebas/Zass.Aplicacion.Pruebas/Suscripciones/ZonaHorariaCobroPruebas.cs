using System.Globalization;
using Xunit.Abstractions;
using Zass.Aplicacion.Pruebas.Dobles;
using Zass.Aplicacion.Puertos;
using Zass.Aplicacion.Suscripciones;
using Zass.Dominio.Comun;
using Zass.Dominio.Identidad;
using Zass.Dominio.Suscripciones;

namespace Zass.Aplicacion.Pruebas.Suscripciones;

/// <summary>
/// T33 - Pruebas de zona horaria hostil para el cobro de S7.
///
/// La agenda usa HoraDeChile.cs, pero Zass.Aplicacion/Suscripciones no la usa nunca.
/// El mes gratis se calcula como ahora.AddDays(plan.DiasPrueba) (ServicioSuscripciones.cs:163),
/// y el cobro automático corre 7 días antes del vencimiento con la clave
/// cobro-automatico:<suscripción>:<vigente-hasta>.
///
/// Si alguna de esas fechas se corta por día en UTC y no en hora de Chile, un fundador
/// inscrito a las 22:00 de Chile (01:00 UTC del día siguiente) puede ganar o perder un día,
/// o cobrarse dos veces.
///
/// DECISION T33 YA TOMADA POR PABLO (18-nov): el mes gratis vence a la MISMA HORA de la
/// inscripción, 30 días exactos después (no a medianoche de Chile).
///
/// La regla del proyecto: una prueba de zona horaria corre bajo un entorno HOSTIL,
/// nunca bajo el correcto. Con el servidor en hora de Chile, el código malo acierta por casualidad.
/// </summary>
public class ZonaHorariaCobroPruebas
{
    private readonly ITestOutputHelper _output;

    public ZonaHorariaCobroPruebas(ITestOutputHelper output)
    {
        _output = output;
    }

    // ================================================================ Prueba del entorno
    //
    // Esta prueba existe para que la corrida del CI muestre cuál era la zona horaria
    // del entorno (TZ=UTC, TZ=Pacific/Kiritimati, etc.). El orquestador no la probó:
    // si algo no funciona, anota el error exacto.

    [Fact]
    public void El_entorno_muestra_la_zona_horaria_local()
    {
        var zona = TimeZoneInfo.Local.Id;
        _output.WriteLine($"TimeZoneInfo.Local.Id = {zona}");
        _output.WriteLine($"TimeZoneInfo.Local.DisplayName = {TimeZoneInfo.Local.DisplayName}");
        _output.WriteLine($"TimeZoneInfo.Local.BaseUtcOffset = {TimeZoneInfo.Local.BaseUtcOffset}");
        _output.WriteLine($"DateTime.Now.Kind = {DateTime.Now.Kind}");
        _output.WriteLine($"DateTime.UtcNow.Kind = {DateTime.UtcNow.Kind}");

        // No hacemos Assert: la prueba siempre pasa y solo informa.
        // El objetivo es que la salida del CI muestre la zona.
        Assert.True(true, $"Zona horaria del entorno: {zona}");
    }

    // ================================================================ Helpers

    /// <summary>
    /// Crea una hora de pared chilena y la convierte a UTC usando HoraDeChile.
    /// Sirve para simular "son las 23:30 en Chile" sin depender de la zona del entorno.
    /// </summary>
    private static DateTime ChileAUtc(int ano, int mes, int dia, int hora, int minuto)
    {
        var utc = HoraDeChile.AUtc(new DateOnly(ano, mes, dia), new TimeOnly(hora, minuto));
        return utc ?? throw new InvalidOperationException($"La hora {hora}:{minuto} del {dia}/{mes}/{ano} no existe en Chile (salto de horario de verano).");
    }

    /// <summary>
    /// Convierte UTC a hora de pared chilena.
    /// </summary>
    private static DateTime UtcAChile(DateTime utc)
    {
        return HoraDeChile.ALocal(utc);
    }

    /// <summary>
    /// Crea la fecha en que vence el mes gratis (30 días exactos después, misma hora).
    /// </summary>
    private static DateTime VencimientoMesGratis(DateTime inscripcionUtc) => inscripcionUtc.AddDays(30);

    /// <summary>
    /// Avanza el reloj fijo al momento especificado.
    /// </summary>
    private static void PonerReloj(RelojFijo reloj, DateTime momento)
    {
        reloj.Avanzar(momento - reloj.Ahora);
    }

    // ================================================================ Escenario base
    //
    // Las pruebas usan el 30 de noviembre de 2026 a las 23:30 de Chile como momento
    // de inscripción del fundador. En UTC eso es 02:30 del 1 de diciembre de 2026
    // (Chile está en horario de verano UTC-3 en noviembre).
    //
    // El mes gratis vence 30 días exactos después a la MISMA HORA: 30 de diciembre
    // a las 23:30 de Chile = 31 de diciembre a las 02:30 UTC.
    //
    // La ventana de cobro (7 días antes) es desde el 23 de diciembre a las 23:30
    // de Chile = 24 de diciembre 02:30 UTC.

    private static readonly DateTime InscripcionChile = ChileAUtc(2026, 11, 30, 23, 30); // 2026-12-01 02:30:00 UTC
    private static readonly DateTime FinMesGratisChile = InscripcionChile.AddDays(30);   // 2027-01-01 02:30:00 UTC
    private static readonly DateTime VentanaCobroInicioChile = FinMesGratisChile.AddDays(-7); // 2026-12-25 02:30:00 UTC

    private BaseEnMemoria MontarBase(out RelojFijo reloj, out PasarelaFalsa pasarela,
        out ServicioSuscripciones suscripciones, out ServicioCobros cobros)
    {
        var baseEnMemoria = new BaseEnMemoria();
        reloj = new RelojFijo(InscripcionChile);
        pasarela = new PasarelaFalsa();

        var repo = new RepositorioSuscripcionesEnMemoria(baseEnMemoria);
        suscripciones = new ServicioSuscripciones(
            repo, baseEnMemoria, reloj, new AuditoriaEnMemoria(), new ColaEnMemoria());
        cobros = new ServicioCobros(
            repo, suscripciones, pasarela, baseEnMemoria, reloj, new AuditoriaEnMemoria());

        baseEnMemoria.Planes.Add(new Plan { Id = 1, Codigo = "prueba", Nombre = "ZASS! Prueba", PrecioMensual = 0, DiasPrueba = 30, Activo = true });
        baseEnMemoria.Planes.Add(new Plan { Id = 2, Codigo = "mensual", Nombre = "ZASS! Mensual", PrecioMensual = 7990, Activo = true });
        baseEnMemoria.Planes.Add(new Plan { Id = 3, Codigo = "fundador", Nombre = "ZASS! Fundador", PrecioMensual = 5990, Activo = true });

        return baseEnMemoria;
    }

    private PerfilPrestador CrearFundadorConMesGratis(
        BaseEnMemoria baseEnMemoria,
        ServicioSuscripciones suscripciones,
        ServicioCobros cobros,
        PasarelaFalsa pasarela,
        RelojFijo reloj,
        DateTime momentoInscripcion)
    {
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        baseEnMemoria.PerfilesPrestador.Add(perfil);

        // Avanzar el reloj al momento exacto de inscripción
        PonerReloj(reloj, momentoInscripcion);

        // 1. Iniciar la prueba (API-22 la llama al publicar el perfil)
        var rPrueba = suscripciones.IniciarPruebaAsync(perfil.Id).GetAwaiter().GetResult();
        Assert.True(rPrueba.Exito);

        // 2. Inscribirse como fundador (toma cupo e inscribe medio de pago)
        var claveInscripcion = "ins-" + perfil.Id;
        var inscripcion = cobros.IniciarInscripcionFundadorAsync(perfil.Id, claveInscripcion).GetAwaiter().GetResult();
        Assert.True(inscripcion.Exito);

        var intento = baseEnMemoria.IntentosDePago.Single(i => i.PerfilPrestadorId == perfil.Id);
        pasarela.Veredicto = new CobroResuelto(intento.Id, "ref-inscripcion", true, 0, null);
        var confirmada = cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>())).GetAwaiter().GetResult();
        Assert.True(confirmada.Exito);

        return perfil;
    }

    // ================================================================ Pruebas de T33

    /// <summary>
    /// RN-S7-03 / DECISION T33: El mes gratis vence a la MISMA HORA de la inscripción,
    /// 30 días exactos después (no a medianoche de Chile).
    ///
    /// Inscrito a las 23:30 de Chile del 30-nov → vence a las 23:30 de Chile del 30-dic
    /// (que es 02:30 UTC del 31-dic / 1-ene).
    ///
    /// Si el código usa DateTime.AddDays() sobre UTC, esto funciona "por casualidad"
    /// cuando el servidor está en Chile. En un entorno hostil (UTC, UTC+14) la fecha
    /// se corta por día UTC y puede ganar/perder un día.
    /// </summary>
    [Fact]
    public async Task Mes_gratis_vence_a_la_misma_hora_de_inscripcion_30_dias_exactos_despues()
    {
        var baseEnMemoria = MontarBase(out var reloj, out var pasarela, out var suscripciones, out var cobros);

        // Crear fundador inscrito a las 23:30 de Chile del 30-nov
        var perfil = CrearFundadorConMesGratis(baseEnMemoria, suscripciones, cobros, pasarela, reloj, InscripcionChile);

        var s = baseEnMemoria.Suscripciones.Single(x => x.PerfilPrestadorId == perfil.Id);

        // La vigencia debe ser EXACTAMENTE 30 días después a la misma hora
        var esperado = VencimientoMesGratis(InscripcionChile);
        Assert.Equal(esperado, s.VigenteHasta);

        // Verificar en hora de Chile: debe ser 30-dic a las 23:30
        var enChile = UtcAChile(s.VigenteHasta);
        Assert.Equal(new DateTime(2026, 12, 30, 23, 30, 0), enChile);
    }

    /// <summary>
    /// La ventana de "7 días antes" en el límite exacto.
    ///
    /// El cobro se dispara cuando quedan <= 7 días. El límite exacto es:
    /// - A las 00:30 de Chile del 24-dic (7 días antes a las 23:30 + 1 hora = día siguiente en Chile)
    /// - A las 23:30 de Chile del 23-dic (exactamente 7 días antes)
    ///
    /// En UTC:
    /// - 23:30 Chile 23-dic = 02:30 UTC 24-dic
    /// - 00:30 Chile 24-dic = 03:30 UTC 24-dic
    ///
    /// Si el código compara solo fechas (DateTime.Date), ambos caen en el mismo día UTC
    /// y la prueba en entorno hostil puede comportarse distinto.
    /// </summary>
    [Theory]
    [InlineData(2026, 12, 23, 23, 30, true, "Exactamente 7 días antes a las 23:30 Chile")]  // límite exacto
    [InlineData(2026, 12, 24, 0, 30, true, "1 hora después del límite (00:30 Chile del 24)")] // dentro de la ventana
    [InlineData(2026, 12, 23, 22, 30, false, "1 hora antes del límite (22:30 Chile del 23)")] // fuera de la ventana
    public async Task Ventana_de_7_dias_antes_en_limite_exacto(
        int ano, int mes, int dia, int hora, int minuto, bool debeCobrar, string descripcion)
    {
        var baseEnMemoria = MontarBase(out var reloj, out var pasarela, out var suscripciones, out var cobros);

        var perfil = CrearFundadorConMesGratis(baseEnMemoria, suscripciones, cobros, pasarela, reloj, InscripcionChile);

        // Poner medio de pago inscrito
        var s = baseEnMemoria.Suscripciones.Single(x => x.PerfilPrestadorId == perfil.Id);
        s.ReferenciaPasarela = "medio-inscrito";

        // Avanzar el reloj al momento de la prueba
        var momentoPrueba = ChileAUtc(ano, mes, dia, hora, minuto);
        PonerReloj(reloj, momentoPrueba);

        var resumen = await cobros.CobrarVencimientosAsync();

        if (debeCobrar)
        {
            Assert.Equal(1, resumen.Cobradas);
            Assert.Equal(0, resumen.Rechazadas);
            _output.WriteLine($"COBRÓ: {descripcion} (momento: {momentoPrueba:o} UTC, {UtcAChile(momentoPrueba):o} Chile)");
        }
        else
        {
            Assert.Equal(0, resumen.Cobradas);
            _output.WriteLine($"NO COBRÓ: {descripcion} (momento: {momentoPrueba:o} UTC, {UtcAChile(momentoPrueba):o} Chile)");
        }
    }

    /// <summary>
    /// Dos corridas del cobro el mismo día de Chile que caen en días UTC distintos:
    /// DEBE haber UN SOLO intento de cobro, y la segunda corrida debe contar YaIntentadas == 1.
    ///
    /// Escenario hostil: el trabajo corre a las 20:30 de Chile (23:30 UTC) y vuelve a
    /// correr a las 23:30 de Chile (02:30 UTC del día SIGUIENTE). En Chile es el MISMO día
    /// (24-dic), pero en UTC son días DISTINTOS (24-dic vs 25-dic).
    ///
    /// La clave de cobro usa vigenteHasta:yyyy-MM-dd (fecha del vencimiento en UTC), no la
    /// fecha de hoy. Como vigenteHasta NO cambia entre las dos corridas (el cobro no se
    /// aprobó todavía), la clave DEBE ser la misma y la segunda corrida debe contar como
    /// YaIntentadas.
    ///
    /// Esta prueba falla si el código usa la fecha de hoy (_reloj.Ahora) en lugar de
    /// vigenteHasta para construir la clave de idempotencia.
    /// </summary>
    [Fact]
    public async Task Dos_corridas_mismo_dia_Chile_dias_UTC_distintos_una_sola_clave()
    {
        var baseEnMemoria = MontarBase(out var reloj, out var pasarela, out var suscripciones, out var cobros);

        // Crear fundador inscrito a las 23:30 de Chile del 30-nov
        var perfil = CrearFundadorConMesGratis(baseEnMemoria, suscripciones, cobros, pasarela, reloj, InscripcionChile);

        var s = baseEnMemoria.Suscripciones.Single(x => x.PerfilPrestadorId == perfil.Id);

        // Poner medio de pago inscrito (necesario para que el cobro intente cobrar)
        s.ReferenciaPasarela = "medio-inscrito";

        // Primera corrida: 20:30 de Chile del 24-dic = 23:30 UTC del 24-dic
        var primeraCorrida = ChileAUtc(2026, 12, 24, 20, 30);
        PonerReloj(reloj, primeraCorrida);
        _output.WriteLine($"Primera corrida: {primeraCorrida:o} UTC ({UtcAChile(primeraCorrida):o} Chile)");

        var resumen1 = await cobros.CobrarVencimientosAsync();
        Assert.Equal(1, resumen1.Cobradas);
        Assert.Equal(0, resumen1.YaIntentadas);

        var intento1 = baseEnMemoria.IntentosDePago.Single(i => i.PerfilPrestadorId == perfil.Id && i.Tipo == TipoDeIntento.Cobro);
        var clave1 = intento1.ClaveIdempotencia;
        _output.WriteLine($"Primera corrida - Clave: {clave1}, Intentos totales: {baseEnMemoria.IntentosDePago.Count(i => i.Tipo == TipoDeIntento.Cobro)}");

        // Rechazar el cobro (para que vigenteHasta NO se mueva y la suscripción siga en ventana)
        pasarela.Veredicto = new CobroResuelto(intento1.Id, "ref-rechazo", false, 0, null);
        var confirmacionRechazo = await cobros.ConfirmarAsync(new AvisoDePasarela(new Dictionary<string, string>()));
        Assert.False(confirmacionRechazo.Exito);

        // Segunda corrida: 23:30 de Chile del 24-dic = 02:30 UTC del 25-dic
        // En Chile es el MISMO día (24-dic), en UTC es el día SIGUIENTE (25-dic)
        var segundaCorrida = ChileAUtc(2026, 12, 24, 23, 30);
        PonerReloj(reloj, segundaCorrida);
        _output.WriteLine($"Segunda corrida: {segundaCorrida:o} UTC ({UtcAChile(segundaCorrida):o} Chile)");

        var resumen2 = await cobros.CobrarVencimientosAsync();

        // La segunda corrida debe contar como YaIntentadas (la clave es la misma)
        Assert.Equal(0, resumen2.Cobradas);
        Assert.Equal(1, resumen2.YaIntentadas);

        // Solo debe haber UN intento de cobro (el de la primera corrida)
        var intentosTotales = baseEnMemoria.IntentosDePago.Count(i => i.PerfilPrestadorId == perfil.Id && i.Tipo == TipoDeIntento.Cobro);
        Assert.Equal(1, intentosTotales);

        var intento2 = baseEnMemoria.IntentosDePago.Single(i => i.PerfilPrestadorId == perfil.Id && i.Tipo == TipoDeIntento.Cobro);
        var clave2 = intento2.ClaveIdempotencia;
        _output.WriteLine($"Segunda corrida - Clave: {clave2}, Intentos totales: {intentosTotales}");

        // Las claves deben ser IGUALES (idempotencia por período, no por día de corrida)
        Assert.Equal(clave1, clave2);
        _output.WriteLine($"Claves iguales: {clave1 == clave2}");
    }

    /// <summary>
    /// Cambio de horario de Chile en S7: el mes gratis de alguien inscrito el día antes
    /// del cambio de horario debe vencer 30 días exactos (720 horas) después, en UTC.
    ///
    /// En 2026:
    /// - 4 de abril: a las 24:00 (medianoche) Chile adelanta el reloj a 01:00 → horario de invierno (UTC-4)
    ///
    /// Si alguien se inscribe el 3 de abril a las 22:00 de Chile (UTC-3), el mes gratis
    /// vence el 3 de mayo a las 22:00 de Chile (UTC-4). En UTC:
    /// - Inscripción: 3 de abril 22:00 Chile = 4 de abril 01:00 UTC
    /// - Vencimiento: 3 de mayo 22:00 Chile = 4 de mayo 02:00 UTC
    ///
    /// La prueba verifica que el código de S7 (ServicioSuscripciones.IniciarPruebaAsync)
    /// usa DateTime.AddDays(30) correctamente, sin cortarse por día UTC.
    /// </summary>
    [Fact]
    public async Task Mes_gratis_cambio_de_horario_chile_s7()
    {
        var baseEnMemoria = MontarBase(out var reloj, out var pasarela, out var suscripciones, out var cobros);

        // Inscripción: 3 de abril de 2026 a las 22:00 de Chile (antes del salto de horario)
        // En UTC: 3 de abril 22:00 Chile (UTC-3) = 4 de abril 01:00 UTC
        var inscripcionChile = ChileAUtc(2026, 4, 3, 22, 0);
        _output.WriteLine($"Inscripción: {inscripcionChile:o} UTC ({UtcAChile(inscripcionChile):o} Chile)");

        // Crear perfil y suscripción
        var perfil = new PerfilPrestador { Id = Guid.NewGuid(), UsuarioId = Guid.NewGuid() };
        baseEnMemoria.PerfilesPrestador.Add(perfil);

        PonerReloj(reloj, inscripcionChile);

        var rPrueba = await suscripciones.IniciarPruebaAsync(perfil.Id);
        Assert.True(rPrueba.Exito);

        var s = baseEnMemoria.Suscripciones.Single(x => x.PerfilPrestadorId == perfil.Id);

        // El vencimiento debe ser 30 días exactos después (720 horas) en UTC
        var vencimientoEsperado = inscripcionChile.AddDays(30);
        Assert.Equal(vencimientoEsperado, s.VigenteHasta);

        // En hora de Chile: debe ser 3 de mayo a las 22:00 (UTC-4 en mayo)
        var enChile = UtcAChile(s.VigenteHasta);
        _output.WriteLine($"Vencimiento: {s.VigenteHasta:o} UTC ({enChile:o} Chile)");
        Assert.Equal(new DateTime(2026, 5, 3, 22, 0, 0), enChile);

        // Verificar que son exactamente 30 días (720 horas) en UTC
        var diferencia = s.VigenteHasta - inscripcionChile;
        Assert.Equal(30, diferencia.Days);
        Assert.Equal(720, diferencia.TotalHours);
    }

    /// <summary>
    /// Prueba de integración completa: inscripción a las 23:30 Chile 30-nov,
    /// cobro a los 7 días antes (23-dic 23:30 Chile), verificación de que:
    /// - El mes gratis venció a la misma hora (30-dic 23:30 Chile)
    /// - La clave de cobro es determinista
    /// - Solo se cobra una vez aunque el trabajo corra dos veces
    /// </summary>
    [Fact]
    public async Task Flujo_completo_inscripcion_cobro_zonas_horarias()
    {
        var baseEnMemoria = MontarBase(out var reloj, out var pasarela, out var suscripciones, out var cobros);

        // 1. Inscripción a las 23:30 Chile del 30-nov
        var perfil = CrearFundadorConMesGratis(baseEnMemoria, suscripciones, cobros, pasarela, reloj, InscripcionChile);

        var s = baseEnMemoria.Suscripciones.Single(x => x.PerfilPrestadorId == perfil.Id);

        // Verificar: mes gratis vence a la misma hora 30 días después
        var finEsperado = VencimientoMesGratis(InscripcionChile);
        Assert.Equal(finEsperado, s.VigenteHasta);

        var finEnChile = UtcAChile(s.VigenteHasta);
        Assert.Equal(new DateTime(2026, 12, 30, 23, 30, 0), finEnChile);
        _output.WriteLine($"Mes gratis vence: {finEnChile:o} Chile ({s.VigenteHasta:o} UTC)");

        // Poner medio de pago
        s.ReferenciaPasarela = "medio-inscrito";

        // 2. Avanzar a 7 días antes del vencimiento: 23-dic 23:30 Chile = 24-dic 02:30 UTC
        var momentoCobro = VentanaCobroInicioChile; // 2026-12-25 02:30:00 UTC = 23-dic 23:30 Chile
        PonerReloj(reloj, momentoCobro);

        _output.WriteLine($"Momento cobro: {momentoCobro:o} UTC ({UtcAChile(momentoCobro):o} Chile)");

        // 3. Primera corrida del cobro
        var resumen1 = await cobros.CobrarVencimientosAsync();
        Assert.Equal(1, resumen1.Cobradas);
        Assert.Equal(0, resumen1.Rechazadas);
        Assert.Equal(0, resumen1.YaIntentadas);

        var intento1 = baseEnMemoria.IntentosDePago.Single(i => i.PerfilPrestadorId == perfil.Id && i.Tipo == TipoDeIntento.Cobro);
        var clave1 = intento1.ClaveIdempotencia;
        _output.WriteLine($"Primera corrida - Clave: {clave1}");

        // 4. Segunda corrida del cobro (mismo día Chile, misma hora UTC en este caso)
        // Nota: tras el primer cobro exitoso, VigenteHasta se mueve 1 mes adelante,
        // así que la suscripción YA NO está en la ventana de "por vencer" (7 días).
        // Por tanto, la segunda corrida no la encuentra y no cuenta como YaIntentadas.
        var resumen2 = await cobros.CobrarVencimientosAsync();
        Assert.Equal(0, resumen2.Cobradas);
        Assert.Equal(0, resumen2.YaIntentadas);

        var intento2 = baseEnMemoria.IntentosDePago.Single(i => i.PerfilPrestadorId == perfil.Id && i.Tipo == TipoDeIntento.Cobro);
        var clave2 = intento2.ClaveIdempotencia;
        _output.WriteLine($"Segunda corrida - Clave: {clave2}");

        // 5. Verificar: MISMA clave (idempotencia por período, no por día de corrida)
        Assert.Equal(clave1, clave2);
        _output.WriteLine($"Claves iguales: {clave1 == clave2}");

        // 6. Verificar que la suscripción se extendió un mes desde el vencimiento original
        var sDespues = baseEnMemoria.Suscripciones.Single(x => x.PerfilPrestadorId == perfil.Id);
        var vigenciaEsperada = finEsperado.AddMonths(1); // 2027-01-31 02:30 UTC = 30-ene 23:30 Chile
        Assert.Equal(vigenciaEsperada, sDespues.VigenteHasta);

        var vigenciaEnChile = UtcAChile(sDespues.VigenteHasta);
        Assert.Equal(new DateTime(2027, 1, 30, 23, 30, 0), vigenciaEnChile);
        _output.WriteLine($"Nueva vigencia: {vigenciaEnChile:o} Chile ({sDespues.VigenteHasta:o} UTC)");
    }
}