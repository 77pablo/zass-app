using Npgsql;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// La migración 20261114204429_IndicesDeClavesForaneas, leída EN LA BASE.
///
/// <para>
/// **Lo que de verdad vigila este archivo, y por qué existe.** PostgreSQL no crea ningún
/// índice al declarar un <c>FOREIGN KEY</c>, y las dos columnas de abajo tenían encima un
/// índice **parcial** que el planificador no puede usar para la comprobación de la clave
/// foránea. Medido con <c>enable_seqscan = off</c> y <c>EXPLAIN</c>: eran <c>Seq Scan</c>.
/// </para>
///
/// <para>
/// **El accidente que esta prueba impide.** Al declarar el índice nuevo en el modelo con la
/// forma obvia —<c>HasIndex(x =&gt; x.BloqueHorarioId)</c>— EF indexa por conjunto de
/// propiedades y **reemplaza** el índice que ya existía sobre esa columna. La migración que
/// generó fue un <c>RenameIndex</c> de <c>ux_cita_bloque_activo</c>, que es la **R-1**: el
/// índice único parcial que impide la doble reserva. Es decir: añadir un índice de
/// rendimiento habría borrado la restricción más importante del sistema, con la migración
/// diciendo «Done» igual que siempre. Se vio leyendo la migración generada antes de
/// aplicarla; estas pruebas son para que la próxima vez no haga falta acordarse de leerla.
/// </para>
/// </summary>
public class MigracionIndicesClavesForaneasPruebas
{
    private const string IndiceDeCitas = "ix_cita_bloque_horario";
    private const string IndiceDeSuscripciones = "ix_suscripcion_perfil_inicia";
    private const string R1 = "ux_cita_bloque_activo";
    private const string UnaSuscripcionVigente = "ux_suscripcion_vigente";

    private static async Task<string?> DefinicionAsync(string tabla, string indice)
    {
        await using var cn = new NpgsqlConnection(Conexion.Cadena);
        await cn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT indexdef FROM pg_indexes WHERE tablename = '{tabla}' AND indexname = '{indice}'",
            cn);
        return await cmd.ExecuteScalarAsync() as string;
    }

    /// <summary>Sin espacios ni castes, que PostgreSQL reescribe a su gusto.</summary>
    private static string Logica(string? sql) => (sql ?? "")
        .Replace("::text", "")
        .Replace("::character varying", "")
        .Replace("(", "")
        .Replace(")", "")
        .Replace(" ", "");

    /// <summary>
    /// Que el indice sea unico, PARCIAL, y que su filtro nombre la columna y los valores que
    /// le importan.
    ///
    /// <para>
    /// No se compara el texto completo del filtro a proposito, y esto costo una carrera roja:
    /// **PostgreSQL 16 y 17 escriben el mismo predicado de forma distinta**. El entorno local
    /// es 16.4, el CI es 17 y produccion (Neon) es 18, asi que una comparacion literal del
    /// `indexdef` solo dice en que version se escribio la prueba. Lo que importa —que el
    /// filtro siga ahi y siga mirando esos estados— se comprueba por contenido.
    /// </para>
    /// </summary>
    private static void EsUnicoYParcialSobre(string? definicion, string columna, params string[] valores)
    {
        Assert.NotNull(definicion);
        Assert.Contains("UNIQUE", definicion!);
        Assert.Contains($"btree{columna}", Logica(definicion));

        // Sin WHERE no es parcial: seria otro indice, con otro significado.
        Assert.Contains("WHERE", definicion!);
        Assert.Contains("estado", Logica(definicion));
        foreach (var valor in valores) Assert.Contains($"'{valor}'", definicion!);
    }

    [Fact]
    public async Task El_indice_de_la_clave_foranea_de_citas_existe_y_NO_es_parcial()
    {
        var definicion = await DefinicionAsync("citas", IndiceDeCitas);

        Assert.NotNull(definicion);
        Assert.Contains("btreebloque_horario_id", Logica(definicion));
        // Si fuera parcial o único no serviría para la comprobación de la clave foránea,
        // que es justo lo que este índice viene a resolver.
        Assert.DoesNotContain("WHERE", definicion!);
        Assert.DoesNotContain("UNIQUE", definicion!);
    }

    /// <summary>
    /// R-1 **sigue ahí**. Esta es la prueba que caza el accidente: si alguien vuelve a
    /// declarar el índice de arriba con la forma que reemplaza al que ya existía, esta cae.
    /// </summary>
    [Fact]
    public async Task Y_la_restriccion_de_la_doble_reserva_sigue_existiendo_intacta()
    {
        var definicion = await DefinicionAsync("citas", R1);

        EsUnicoYParcialSobre(definicion, "bloque_horario_id", "Solicitada", "Confirmada");
    }

    [Fact]
    public async Task El_indice_de_suscripciones_ordena_por_inicia_en_descendente()
    {
        var definicion = await DefinicionAsync("suscripciones", IndiceDeSuscripciones);

        Assert.NotNull(definicion);
        // El DESC no es decoración: es lo que permite que «¿este prestador está al día?»
        // —un ORDER BY inicia_en DESC LIMIT 1— salga del índice sin ordenar nada.
        Assert.Contains("btreeperfil_prestador_id,inicia_enDESC", Logica(definicion));
        Assert.DoesNotContain("UNIQUE", definicion!);
    }

    /// <summary>El hermano del candado: el mismo riesgo existía en `suscripciones`.</summary>
    [Fact]
    public async Task Y_la_de_una_sola_suscripcion_vigente_sigue_existiendo_intacta()
    {
        var definicion = await DefinicionAsync("suscripciones", UnaSuscripcionVigente);

        EsUnicoYParcialSobre(definicion, "perfil_prestador_id", "Prueba", "Activa");
    }

    /// <summary>
    /// Y que la migración esté aplicada de verdad en esta base, con su identificador exacto:
    /// si no lo estuviera, las cuatro pruebas de arriba estarían mirando otra cosa.
    /// </summary>
    [Fact]
    public async Task La_migracion_figura_aplicada()
    {
        await using var cn = new NpgsqlConnection(Conexion.Cadena);
        await cn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT "MigrationId" FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = '20261114204429_IndicesDeClavesForaneas'
            """, cn);

        Assert.Equal("20261114204429_IndicesDeClavesForaneas", await cmd.ExecuteScalarAsync() as string);
    }
}
