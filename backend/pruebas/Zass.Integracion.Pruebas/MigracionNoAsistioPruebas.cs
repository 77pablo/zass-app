using Npgsql;

namespace Zass.Integracion.Pruebas;

/// <summary>
/// La migración 20261112211216_NoAsistioQuienFalto leída EN LA BASE, no en el archivo.
///
/// Que una migración compile y que EF la marque como aplicada no dice que el objeto quedara
/// como se escribió: un <c>HasFilter</c> mal puesto, un <c>HasMaxLength</c> que el proveedor
/// ignora o un CHECK que se quedó en el `ModelSnapshot` y no en el `Up` dan exactamente el
/// mismo «Done». Esto pregunta al catálogo de PostgreSQL y compara contra lo que se esperaba,
/// con nombre y definición.
///
/// Las definiciones se comparan NORMALIZADAS (sin espacios, paréntesis ni castes `::text`)
/// porque PostgreSQL reescribe el texto que uno le dio. Lo que no se normaliza es el operador
/// ni los operandos: cambiar el <c>OR</c> del CHECK por un <c>AND</c>, o el valor del filtro
/// del índice, pone esto en rojo.
/// </summary>
public class MigracionNoAsistioPruebas
{
    private const string Tabla = "citas";
    private const string Columna = "quien_falto";
    private const string NombreDelCheck = "ck_cita_quien_falto";
    private const string NombreDelIndice = "ix_cita_en_disputa";

    private static async Task<string?> UnTextoAsync(string sql)
    {
        await using var cn = new NpgsqlConnection(Conexion.Cadena);
        await cn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        return await cmd.ExecuteScalarAsync() as string;
    }

    /// <summary>
    /// Quita lo que PostgreSQL añade al guardar una expresión y deja la lógica desnuda.
    /// `((quien_falto)::text = 'Nadie'::text)` queda como `quien_falto='Nadie'`.
    /// </summary>
    private static string Logica(string? sql) => (sql ?? "")
        .Replace("::text", "")
        .Replace("::character varying", "")
        .Replace("(", "")
        .Replace(")", "")
        .Replace(" ", "")
        .Replace("CHECK", "");

    /// <summary>
    /// La columna: texto de 20, obligatoria y con 'Nadie' por omisión. El valor por omisión
    /// es lo que hace que la migración no rompa las citas que ya existían.
    /// </summary>
    [Fact]
    public async Task La_columna_quien_falto_esta_como_la_escribio_la_migracion()
    {
        var fila = await UnTextoAsync(
            $"""
             SELECT data_type || '|' || coalesce(character_maximum_length::text, '-')
                    || '|' || is_nullable || '|' || coalesce(column_default, '-')
             FROM information_schema.columns
             WHERE table_name = '{Tabla}' AND column_name = '{Columna}'
             """);

        Assert.NotNull(fila);
        var partes = fila!.Split('|');
        Assert.Equal("character varying", partes[0]);
        Assert.Equal("20", partes[1]);
        Assert.Equal("NO", partes[2]);
        Assert.Equal("'Nadie'", Logica(partes[3]));
    }

    /// <summary>
    /// El CHECK que impide el estado imposible. Se compara la expresión completa a propósito:
    /// un CHECK con el nombre correcto y la condición invertida protegería lo contrario de lo
    /// que dice protegerse, y la base lo aceptaría igual de contenta.
    /// </summary>
    [Fact]
    public async Task El_check_del_estado_imposible_existe_y_dice_lo_que_tiene_que_decir()
    {
        var definicion = await UnTextoAsync(
            $"""
             SELECT pg_get_constraintdef(oid)
             FROM pg_constraint
             WHERE conrelid = '{Tabla}'::regclass
               AND contype = 'c'
               AND conname = '{NombreDelCheck}'
             """);

        Assert.NotNull(definicion);
        Assert.Equal("quien_falto='Nadie'ORestado='NoAsistio'", Logica(definicion));
    }

    /// <summary>
    /// El índice de la cola interna de revisión, y que sea PARCIAL. Sin el filtro seguiría
    /// existiendo un índice con ese nombre, la consulta seguiría funcionando y nadie se
    /// enteraría de que se indexó la columna entera para encontrar una minoría de filas.
    /// </summary>
    [Fact]
    public async Task El_indice_de_la_cola_de_revision_existe_y_es_parcial()
    {
        var definicion = await UnTextoAsync(
            $"""
             SELECT indexdef FROM pg_indexes
             WHERE tablename = '{Tabla}' AND indexname = '{NombreDelIndice}'
             """);

        Assert.NotNull(definicion);
        var logica = Logica(definicion);
        Assert.Contains($"USINGbtreequien_falto", logica);
        Assert.Contains("WHEREquien_falto='Discrepancia'", logica);
        Assert.DoesNotContain("UNIQUE", definicion!);
    }

    /// <summary>
    /// Y que la migración esté de verdad aplicada en esta base, con su identificador exacto:
    /// si no lo estuviera, las tres pruebas de arriba estarían mirando otra cosa.
    /// </summary>
    [Fact]
    public async Task La_migracion_esta_aplicada_en_esta_base()
    {
        var id = await UnTextoAsync(
            """
            SELECT "MigrationId" FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = '20261112211216_NoAsistioQuienFalto'
            """);

        Assert.Equal("20261112211216_NoAsistioQuienFalto", id);
    }
}

/// <summary>
/// `ck_cita_quien_falto` EJECUTÁNDOSE: que la base rechace de verdad los estados imposibles,
/// no que el CHECK esté escrito.
///
/// <para>
/// Las filas se insertan en una tabla TEMPORAL creada con
/// <c>CREATE TEMP TABLE … (LIKE citas INCLUDING ALL)</c>. PostgreSQL copia ahí los CHECK y los
/// índices de <c>citas</c> —con su nombre y su expresión, tal como las tiene guardadas— pero no
/// las claves ajenas. Eso es lo que hace la prueba posible: una fila de <c>citas</c> exige un
/// perfil de prestador, y <c>perfiles_prestador</c> tiene una columna geography generada, así
/// que insertar un perfil necesita PostGIS. Mientras PostGIS no cargue en esta máquina, el
/// camino completo no se puede recorrer.
/// </para>
///
/// <para>
/// Lo que esto SÍ mide: la expresión real que la migración dejó en la base, evaluada por
/// PostgreSQL, sobre los tipos reales de las columnas. Si alguien quita el CHECK de
/// <c>citas</c>, invierte su condición o cambia un valor, la copia hereda el cambio y esto se
/// pone rojo. Lo que NO mide: que el CHECK esté colgado de <c>citas</c> y no de otra tabla —eso
/// lo afirma <see cref="MigracionNoAsistioPruebas.El_check_del_estado_imposible_existe_y_dice_lo_que_tiene_que_decir"/>,
/// por nombre y por definición. Las dos juntas cubren lo que cubriría una sola prueba
/// end-to-end; separadas porque la máquina no da para otra cosa.
/// </para>
/// </summary>
public class CheckDelEstadoImposiblePruebas
{
    private const string Copia = "CREATE TEMP TABLE citas_copia (LIKE citas INCLUDING ALL)";

    private const string Insertar =
        """
        INSERT INTO citas_copia
            (id, codigo, bloque_horario_id, buscador_id, prestador_servicio_id,
             perfil_prestador_id, estado, modo, version, creada_en, quien_falto)
        VALUES
            (gen_random_uuid(), left(gen_random_uuid()::text, 12), gen_random_uuid(),
             gen_random_uuid(), gen_random_uuid(), gen_random_uuid(),
             @estado, 'Automatico', 1, now(), @quien)
        """;

    /// <summary>
    /// Abre una conexión con una transacción que SIEMPRE se deshace, y con la copia de
    /// <c>citas</c> ya creada dentro.
    /// </summary>
    private static async Task<(NpgsqlConnection Cn, NpgsqlTransaction Tx)> ConLaCopiaAsync()
    {
        var cn = new NpgsqlConnection(Conexion.Cadena);
        await cn.OpenAsync();
        var tx = await cn.BeginTransactionAsync();
        await using (var crear = new NpgsqlCommand(Copia, cn, tx))
            await crear.ExecuteNonQueryAsync();
        return (cn, tx);
    }

    private static async Task InsertarAsync(
        NpgsqlConnection cn, NpgsqlTransaction tx, string estado, string quienFalto)
    {
        await using var cmd = new NpgsqlCommand(Insertar, cn, tx);
        cmd.Parameters.AddWithValue("estado", estado);
        cmd.Parameters.AddWithValue("quien", quienFalto);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Lo imposible: alguien marcado como ausente en una cita que no quedó en `no_asistio`.
    /// Es el estado que haría que la elegibilidad de reseña dijera «sí» justo donde la decisión
    /// del 11-nov-2026 dice «no», y la base no lo deja escribir.
    /// </summary>
    [Theory]
    [InlineData("Solicitada", "Cliente")]
    [InlineData("Confirmada", "Prestador")]
    [InlineData("PendienteAsistencia", "Cliente")]
    [InlineData("Realizada", "Cliente")]
    [InlineData("Realizada", "Prestador")]
    [InlineData("Realizada", "Discrepancia")]
    [InlineData("Cancelada", "Prestador")]
    [InlineData("Rechazada", "Cliente")]
    [InlineData("Reprogramada", "Discrepancia")]
    public async Task La_base_rechaza_un_ausente_en_una_cita_que_no_es_no_asistio(
        string estado, string quienFalto)
    {
        var (cn, tx) = await ConLaCopiaAsync();
        await using var _ = cn;
        await using var __ = tx;

        var pg = await Assert.ThrowsAsync<PostgresException>(
            () => InsertarAsync(cn, tx, estado, quienFalto));

        Assert.Equal(PostgresErrorCodes.CheckViolation, pg.SqlState);
        Assert.Equal("ck_cita_quien_falto", pg.ConstraintName);
    }

    /// <summary>
    /// El contrapunto, y el que impide que el CHECK sea de más: con la cita en `no_asistio`
    /// cabe cualquiera de los cuatro desenlaces, y con `Nadie` cabe cualquier estado. Sin esto,
    /// un CHECK con un <c>AND</c> donde va el <c>OR</c> pasaría la prueba de arriba y rompería
    /// el producto entero: no se podría guardar ninguna cita.
    /// </summary>
    [Theory]
    [InlineData("NoAsistio", "Nadie")]
    [InlineData("NoAsistio", "Cliente")]
    [InlineData("NoAsistio", "Prestador")]
    [InlineData("NoAsistio", "Discrepancia")]
    [InlineData("Solicitada", "Nadie")]
    [InlineData("Confirmada", "Nadie")]
    [InlineData("PendienteAsistencia", "Nadie")]
    [InlineData("Realizada", "Nadie")]
    [InlineData("Cancelada", "Nadie")]
    public async Task La_base_acepta_las_combinaciones_que_si_pueden_existir(
        string estado, string quienFalto)
    {
        var (cn, tx) = await ConLaCopiaAsync();
        await using var _ = cn;
        await using var __ = tx;

        await InsertarAsync(cn, tx, estado, quienFalto);   // no debe lanzar

        await using var contar = new NpgsqlCommand(
            "SELECT count(*) FROM citas_copia WHERE estado = @e AND quien_falto = @q", cn, tx);
        contar.Parameters.AddWithValue("e", estado);
        contar.Parameters.AddWithValue("q", quienFalto);
        Assert.Equal(1L, await contar.ExecuteScalarAsync());
    }

    /// <summary>
    /// El caso por el que el CHECK existe, y que no es el INSERT: una cita ya acusada que
    /// alguien mueve a `realizada` sin limpiar la columna. Es exactamente lo que haría una ruta
    /// nueva escrita por quien no conozca esta regla, y es el único momento en que el CHECK
    /// salva algo que el dominio no estaba mirando.
    /// </summary>
    [Theory]
    [InlineData("Realizada")]
    [InlineData("Cancelada")]
    [InlineData("Confirmada")]
    public async Task La_base_rechaza_mover_una_cita_acusada_a_otro_estado_sin_limpiar_el_ausente(
        string destino)
    {
        var (cn, tx) = await ConLaCopiaAsync();
        await using var _ = cn;
        await using var __ = tx;

        await InsertarAsync(cn, tx, "NoAsistio", "Cliente");

        await using var mover = new NpgsqlCommand(
            "UPDATE citas_copia SET estado = @e WHERE quien_falto = 'Cliente'", cn, tx);
        mover.Parameters.AddWithValue("e", destino);

        var pg = await Assert.ThrowsAsync<PostgresException>(() => mover.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.CheckViolation, pg.SqlState);
        Assert.Equal("ck_cita_quien_falto", pg.ConstraintName);
    }

    /// <summary>
    /// Y el camino que sí está permitido: mover la cita a otro estado LIMPIANDO la columna en
    /// la misma sentencia. El CHECK no bloquea corregir, bloquea dejar la contradicción.
    /// </summary>
    [Fact]
    public async Task La_base_deja_mover_la_cita_si_se_limpia_el_ausente_a_la_vez()
    {
        var (cn, tx) = await ConLaCopiaAsync();
        await using var _ = cn;
        await using var __ = tx;

        await InsertarAsync(cn, tx, "NoAsistio", "Cliente");

        await using var mover = new NpgsqlCommand(
            "UPDATE citas_copia SET estado = 'Realizada', quien_falto = 'Nadie'", cn, tx);
        Assert.Equal(1, await mover.ExecuteNonQueryAsync());
    }
}
