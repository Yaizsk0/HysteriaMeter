using System.Globalization;
using System.Text.Json;
using Dapper;
using HysteriaMeter.Combat;
using Microsoft.Data.Sqlite;

namespace HysteriaMeter.Storage;

/// <summary>Historial local de encuentros en SQLite.</summary>
public sealed class EncounterRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    // El pipeline y la limpieza del historial pueden inicializar a la vez: las migraciones corren una sola vez.
    private readonly Lock _initLock = new();
    private Task? _init;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_initLock)
        {
            if (_init is null || _init.IsFaulted || _init.IsCanceled)
            {
                _init = MigrateAsync(cancellationToken);
            }

            return _init;
        }
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS encounters (
                id            TEXT PRIMARY KEY,
                started_at    TEXT NOT NULL,
                ended_at      TEXT NOT NULL,
                target_name   TEXT,
                target_killed INTEGER NOT NULL,
                total_damage  INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS encounter_actors (
                encounter_id  TEXT NOT NULL REFERENCES encounters(id) ON DELETE CASCADE,
                actor_id      INTEGER NOT NULL,
                name          TEXT NOT NULL,
                class_id      INTEGER,
                is_local      INTEGER NOT NULL,
                damage        INTEGER NOT NULL,
                dps           REAL NOT NULL,
                hits          INTEGER NOT NULL,
                crits         INTEGER NOT NULL,
                max_hit       INTEGER NOT NULL,
                healing       INTEGER NOT NULL,
                skills_json   TEXT NOT NULL,
                PRIMARY KEY (encounter_id, actor_id)
            );
            CREATE INDEX IF NOT EXISTS ix_encounters_started ON encounters(started_at DESC);
            """).ConfigureAwait(false);

        // Migraciones: columnas agregadas después de la primera versión. Las bases viejas las reciben vacías.
        await AddColumnIfMissingAsync(connection, "encounters", "main_target_seconds", "REAL").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounter_actors", "main_target_damage", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounter_actors", "main_target_dps", "REAL NOT NULL DEFAULT 0").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounter_actors", "periodic_damage", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounter_actors", "active_dps", "REAL NOT NULL DEFAULT 0").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounter_actors", "counts_json", "TEXT").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounter_actors", "overkill", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounter_actors", "timeline_json", "TEXT").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounters", "note", "TEXT").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounters", "deaths_json", "TEXT").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounter_actors", "damage_taken", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounter_actors", "hits_taken", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounter_actors", "deaths", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounters", "zone_entered_at", "TEXT").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounters", "marks_json", "TEXT").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounters", "phases_json", "TEXT").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounter_actors", "item_level", "INTEGER").ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "encounter_actors", "combat_power", "INTEGER").ConfigureAwait(false);
        // Plantilla del objetivo: sin nombre en data/npcs.json, los intentos se agrupan por este ID. Las peleas viejas quedan en NULL.
        await AddColumnIfMissingAsync(connection, "encounters", "target_npc_id", "INTEGER").ConfigureAwait(false);
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_encounters_target ON encounters(target_name, started_at)").ConfigureAwait(false);
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_encounters_npc ON encounters(target_npc_id, started_at)").ConfigureAwait(false);
    }

    private static async Task AddColumnIfMissingAsync(SqliteConnection connection, string table, string column, string definition)
    {
        var columns = await connection.QueryAsync<string>($"SELECT name FROM pragma_table_info('{table}')").ConfigureAwait(false);
        if (!columns.Contains(column, StringComparer.Ordinal))
        {
            await connection.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}").ConfigureAwait(false);
        }
    }

    /// <summary>Una pelea terminó de guardarse; los consumidores pueden volver a consultar sin adelantar al commit.</summary>
    public event Action? Saved;

    public async Task SaveAsync(EncounterSnapshot encounter, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            """
            INSERT OR REPLACE INTO encounters (id, started_at, ended_at, target_name, target_killed, total_damage, main_target_seconds, deaths_json,
                                               zone_entered_at, marks_json, phases_json, target_npc_id)
            VALUES (@Id, @Start, @End, @TargetName, @TargetKilled, @TotalDamage, @MainTargetSeconds, @DeathsJson, @ZoneEnteredAt, @MarksJson, @PhasesJson,
                    @TargetNpcId)
            """,
            new
            {
                Id = encounter.Id.ToString(),
                Start = encounter.Start.ToString("O", CultureInfo.InvariantCulture),
                End = encounter.End.ToString("O", CultureInfo.InvariantCulture),
                encounter.TargetName,
                encounter.TargetKilled,
                encounter.TotalDamage,
                MainTargetSeconds = encounter.MainTargetDuration?.TotalSeconds,
                DeathsJson = encounter.Deaths is { Count: > 0 } deaths ? JsonSerializer.Serialize(deaths) : null,
                ZoneEnteredAt = encounter.ZoneEnteredAt?.ToString("O", CultureInfo.InvariantCulture),
                MarksJson = encounter.Marks is { Count: > 0 } marks ? JsonSerializer.Serialize(marks) : null,
                PhasesJson = encounter.Phases is { Count: > 0 } phases ? JsonSerializer.Serialize(phases) : null,
                TargetNpcId = (long?)encounter.TargetNpcId,
            },
            transaction).ConfigureAwait(false);

        await connection.ExecuteAsync(
            """
            INSERT OR REPLACE INTO encounter_actors
                (encounter_id, actor_id, name, class_id, is_local, damage, dps, hits, crits, max_hit, healing, skills_json,
                 main_target_damage, main_target_dps, periodic_damage, active_dps, counts_json, overkill, timeline_json,
                 damage_taken, hits_taken, deaths, item_level, combat_power)
            VALUES (@EncounterId, @ActorId, @Name, @ClassId, @IsLocal, @Damage, @Dps, @Hits, @Crits, @MaxHit, @Healing, @SkillsJson,
                    @MainTargetDamage, @MainTargetDps, @PeriodicDamage, @ActiveDps, @CountsJson, @Overkill, @TimelineJson,
                    @DamageTaken, @HitsTaken, @Deaths, @ItemLevel, @CombatPower)
            """,
            encounter.Actors.Select(a => new
            {
                EncounterId = encounter.Id.ToString(),
                ActorId = (long)a.Id,
                a.Name,
                a.ClassId,
                IsLocal = a.IsLocalPlayer,
                a.Damage,
                a.Dps,
                a.Hits,
                a.Crits,
                a.MaxHit,
                a.Healing,
                SkillsJson = JsonSerializer.Serialize(a.Skills),
                a.MainTargetDamage,
                a.MainTargetDps,
                a.PeriodicDamage,
                a.ActiveDps,
                CountsJson = a.Counts is null ? null : JsonSerializer.Serialize(a.Counts),
                a.Overkill,
                TimelineJson = a.Timeline is null ? null : JsonSerializer.Serialize(a.Timeline),
                a.ItemLevel,
                a.CombatPower,
                a.DamageTaken,
                a.HitsTaken,
                a.Deaths,
            }),
            transaction).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        Saved?.Invoke();
    }

    public Task<IReadOnlyList<EncounterSummary>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        QuerySummariesAsync(string.Empty, new { limit }, cancellationToken);

    /// <summary>
    /// Una pelea guardada como la lista el historial (con su intento, récord y nota), aunque haya quedado fuera de las
    /// últimas que carga la ventana. <c>null</c> si no existe.
    /// </summary>
    public async Task<EncounterSummary?> LoadSummaryAsync(string id, CancellationToken cancellationToken = default) =>
        (await QuerySummariesAsync("WHERE e.id = @id", new { id, limit = 1 }, cancellationToken).ConfigureAwait(false)) is [var summary] ? summary : null;

    private async Task<IReadOnlyList<EncounterSummary>> QuerySummariesAsync(string filter, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        // Intento: cuántas veces se peleó contra ese objetivo hasta esta pelea (todas, trash incluido). Récord: el mejor DPS
        // propio entre los intentos de boss contra ese objetivo (BossAttemptOf: un pull corto no se lo lleva).
        // Los dos son por personaje: con varios personajes, cada uno compite contra sí mismo ("IS" compara también null).
        // "Ese objetivo" es la misma plantilla de NPC o, sin plantilla, el mismo nombre (FightTarget.Identity). Sin nombre ni
        // plantilla (peleas viejas) no hay con qué comparar: intento 0 y sin récord.
        // Las sumas de toda la party (daño, daño al objetivo principal, muertes) alcanzan para clasificar la pelea con
        // BossRules sin cargar cada una entera.
        var rows = await connection.QueryAsync<EncounterRow>(
            $"""
            WITH loc AS (SELECT encounter_id, MAX(dps) AS dps, MAX(name) AS name FROM encounter_actors WHERE is_local = 1 GROUP BY encounter_id),
                 party AS (SELECT encounter_id, SUM(damage) AS damage, SUM(main_target_damage) AS main_damage, SUM(deaths) AS deaths
                           FROM encounter_actors GROUP BY encounter_id)
            SELECT e.id AS Id, e.started_at AS StartedAt, e.ended_at AS EndedAt, e.target_name AS TargetName,
                   e.target_killed AS TargetKilled, e.total_damage AS TotalDamage, e.note AS Note,
                   l.dps AS LocalDps,
                   (SELECT MAX(l2.dps) FROM loc l2 JOIN encounters o ON o.id = l2.encounter_id
                     WHERE {SameTarget} AND {BossAttemptOf("o")} AND l2.name IS l.name) AS BestLocalDps,
                   (SELECT COUNT(*) FROM encounters o LEFT JOIN loc l2 ON l2.encounter_id = o.id
                     WHERE {SameTarget} AND o.started_at <= e.started_at AND l2.name IS l.name) AS Attempt,
                   e.zone_entered_at AS ZoneEnteredAt,
                   l.name AS LocalName,
                   e.main_target_seconds AS MainTargetSeconds,
                   p.damage AS ActorsDamage, p.main_damage AS MainTargetDamage, p.deaths AS Deaths,
                   e.target_npc_id AS TargetNpcId
            FROM encounters e LEFT JOIN loc l ON l.encounter_id = e.id LEFT JOIN party p ON p.encounter_id = e.id
            {filter}
            ORDER BY e.started_at DESC LIMIT @limit
            """,
            parameters).ConfigureAwait(false);
        return [.. rows.Select(r => new EncounterSummary(
            r.Id,
            r.StartedAt,
            r.EndedAt,
            r.TargetName,
            r.TargetKilled != 0,
            r.TotalDamage,
            r.Note,
            r.LocalDps,
            FightTarget.Identity(r.TargetName, r.TargetNpcId is { } id ? (uint)id : null) is null ? 0 : (int)r.Attempt,
            r.LocalDps is { } dps && r.BestLocalDps is { } best && dps >= best,
            r.ZoneEnteredAt,
            r.LocalName,
            r.MainTargetSeconds,
            r.ActorsDamage ?? 0,
            r.MainTargetDamage ?? 0,
            r.Deaths is { } deaths ? (int)deaths : null,
            r.TargetNpcId is { } npc ? (uint)npc : null))];
    }

    /// <summary>
    /// La pelea <c>o</c> es contra el mismo objetivo que <c>e</c>, con la regla de <see cref="FightTarget.Identity(string?, uint?)"/>:
    /// la misma plantilla de NPC si <c>e</c> la tiene (aunque una tenga nombre y la otra no: ponerle nombre en data/npcs.json
    /// no corta los intentos); sin plantilla, el mismo nombre entre peleas sin plantilla. Los "#entidad" de versiones viejas
    /// y el nombre vacío no son nombres. Sin nombre ni plantilla no coincide con nada (<c>=</c> con NULL es falso).
    /// </summary>
    private const string SameTarget =
        """
        ((e.target_npc_id IS NOT NULL AND o.target_npc_id = e.target_npc_id)
         OR (e.target_npc_id IS NULL AND o.target_npc_id IS NULL AND e.target_name <> '' AND substr(e.target_name, 1, 1) <> '#'
             AND o.target_name = e.target_name))
        """;

    /// <summary>
    /// La regla de <see cref="SameTarget"/> contra un objetivo que llega por parámetros (<c>@npcId</c>, <c>@targetName</c>)
    /// en vez de otra fila: la misma plantilla si la hay; sin plantilla, el mismo nombre entre las peleas sin plantilla.
    /// <paramref name="alias"/> es la tabla de encuentros de la consulta.
    /// </summary>
    private static string TargetIs(string alias) =>
        $"((@npcId IS NOT NULL AND {alias}.target_npc_id = @npcId) OR (@npcId IS NULL AND {alias}.target_npc_id IS NULL AND {alias}.target_name = @targetName))";

    /// <summary>
    /// <see cref="TargetIs"/> y, además, que la pelea sea un intento de boss (<see cref="BossAttemptOf"/>): con lo que se
    /// comparan el récord, el intento anterior, el percentil y "Comparar con" del camino en vivo.
    /// </summary>
    private static string ComparedAttempt(string alias) => $"{TargetIs(alias)} AND {BossAttemptOf(alias)}";

    /// <summary>
    /// La pelea <paramref name="alias"/> es un intento contra un boss, con la regla de
    /// <see cref="BossRules.IsBossAttempt(bool, bool, bool, TimeSpan, TimeSpan?, long, long)"/> como la aplica el historial
    /// (HistoryItem: la marca de boss de data/npcs.json no se guarda): un kill de al menos 30 s contra el objetivo principal,
    /// o una pelea sin kill de al menos 30 s en la que el objetivo principal se llevó la mayor parte del daño. Un pull corto
    /// o con el daño repartido es trash y no entra en las comparaciones (su DPS de arranque no es un récord). El objetivo ya
    /// lo filtran <see cref="TargetIs"/> o <see cref="SameTarget"/>: tiene nombre o plantilla. Los segundos se redondean a
    /// milésimas porque julianday da 29,999998 para 30 s justos.
    /// </summary>
    private static string BossAttemptOf(string alias)
    {
        var seconds = $"ROUND((julianday({alias}.ended_at) - julianday({alias}.started_at)) * 86400, 3)";
        var minimum = BossRules.BossKillMinDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        var share = BossRules.BossFightShare.ToString(CultureInfo.InvariantCulture);
        return $"""
            (({alias}.target_killed = 1 AND COALESCE(ROUND({alias}.main_target_seconds, 3), {seconds}) >= {minimum})
             OR ({alias}.target_killed = 0 AND {seconds} >= {minimum}
                 AND EXISTS (SELECT 1 FROM encounter_actors x WHERE x.encounter_id = {alias}.id GROUP BY x.encounter_id
                             HAVING SUM(x.damage) > 0 AND SUM(x.main_target_damage) >= {share} * SUM(x.damage))))
            """;
    }

    /// <summary>Filtro de las consultas viejas por nombre: solo el texto guardado, sin mirar la plantilla.</summary>
    private const string NameIs = "e.target_name = @targetName";

    /// <summary>
    /// El objetivo como parámetros de <see cref="TargetIs"/>: el nombre de verdad (<see cref="FightTarget.Name"/>) y la
    /// plantilla. <c>false</c> sin ninguno de los dos: no se compara con nada, como en el historial.
    /// </summary>
    private static bool TryTarget(string? targetName, uint? npcId, out string? name, out long? npc)
    {
        name = FightTarget.Name(targetName);
        npc = npcId;
        return FightTarget.Identity(targetName, npcId) is not null;
    }

    /// <summary>
    /// Mejor DPS propio contra ese objetivo, sin contar la pelea <paramref name="excludeId"/>. <c>null</c> si no hay otra.
    /// Con <paramref name="localName"/>, solo las peleas con ese personaje. Compara solo por el nombre guardado: para la
    /// regla del historial (plantilla primero) va la sobrecarga con la plantilla.
    /// </summary>
    public Task<double?> BestLocalDpsAsync(string targetName, string excludeId, string? localName = null, CancellationToken cancellationToken = default) =>
        BestLocalDpsCoreAsync(NameIs, targetName, null, excludeId, localName, cancellationToken);

    /// <summary>
    /// Mejor DPS propio contra ese objetivo con la regla de <see cref="FightTarget.Identity(string?, uint?)"/> (la
    /// plantilla primero y, sin plantilla, el nombre) entre los intentos de boss (<see cref="BossAttemptOf"/>), sin contar
    /// <paramref name="excludeId"/>. <c>null</c> si no hay otro o si el objetivo no tiene nombre ni plantilla.
    /// </summary>
    public Task<double?> BestLocalDpsAsync(string? targetName, uint? npcId, string excludeId, string? localName = null, CancellationToken cancellationToken = default) =>
        TryTarget(targetName, npcId, out var name, out var npc)
            ? BestLocalDpsCoreAsync(ComparedAttempt("e"), name, npc, excludeId, localName, cancellationToken)
            : Task.FromResult<double?>(null);

    private async Task<double?> BestLocalDpsCoreAsync(
        string target, string? targetName, long? npcId, string excludeId, string? localName, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<double?>(
            $"""
            SELECT MAX(a.dps) FROM encounter_actors a JOIN encounters e ON e.id = a.encounter_id
            WHERE a.is_local = 1 AND {target} AND e.id <> @excludeId AND (@localName IS NULL OR a.name = @localName)
            """,
            new { targetName, npcId, excludeId, localName }).ConfigureAwait(false);
    }

    /// <summary>
    /// DPS propio en el intento anterior contra ese objetivo (el último antes de <paramref name="excludeId"/>). <c>null</c>
    /// si no hay. Compara solo por el nombre guardado.
    /// </summary>
    public Task<double?> PreviousLocalDpsAsync(string targetName, string excludeId, string? localName = null, CancellationToken cancellationToken = default) =>
        PreviousLocalDpsCoreAsync(NameIs, targetName, null, excludeId, localName, cancellationToken);

    /// <summary>
    /// DPS propio en el intento de boss anterior contra ese objetivo con la regla de <see cref="FightTarget.Identity(string?, uint?)"/>
    /// (un pull corto que quedó como trash no cuenta, <see cref="BossAttemptOf"/>).
    /// <c>null</c> si no hay o si el objetivo no tiene nombre ni plantilla.
    /// </summary>
    public Task<double?> PreviousLocalDpsAsync(string? targetName, uint? npcId, string excludeId, string? localName = null, CancellationToken cancellationToken = default) =>
        TryTarget(targetName, npcId, out var name, out var npc)
            ? PreviousLocalDpsCoreAsync(ComparedAttempt("e"), name, npc, excludeId, localName, cancellationToken)
            : Task.FromResult<double?>(null);

    private async Task<double?> PreviousLocalDpsCoreAsync(
        string target, string? targetName, long? npcId, string excludeId, string? localName, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<double?>(
            $"""
            SELECT a.dps FROM encounter_actors a JOIN encounters e ON e.id = a.encounter_id
            WHERE a.is_local = 1 AND {target} AND e.id <> @excludeId AND (@localName IS NULL OR a.name = @localName)
              AND e.started_at < COALESCE((SELECT started_at FROM encounters WHERE id = @excludeId), '9999')
            ORDER BY e.started_at DESC LIMIT 1
            """,
            new { targetName, npcId, excludeId, localName }).ConfigureAwait(false);
    }

    /// <summary>
    /// Tu DPS en cada pelea guardada contra ese objetivo, sin contar <paramref name="excludeId"/>: con la pelea actual,
    /// da el percentil del detalle ("entre tus N intentos"). Con <paramref name="localName"/>, solo las de ese personaje.
    /// Compara solo por el nombre guardado.
    /// </summary>
    public Task<IReadOnlyList<double>> LocalDpsAsync(string targetName, string excludeId, string? localName = null, CancellationToken cancellationToken = default) =>
        LocalDpsCoreAsync(NameIs, targetName, null, excludeId, localName, cancellationToken);

    /// <summary>
    /// Tu DPS en cada intento de boss guardado contra ese objetivo con la regla de <see cref="FightTarget.Identity(string?, uint?)"/>,
    /// como el percentil del historial (sin los pulls de trash, <see cref="BossAttemptOf"/>).
    /// Vacío si el objetivo no tiene nombre ni plantilla.
    /// </summary>
    public Task<IReadOnlyList<double>> LocalDpsAsync(string? targetName, uint? npcId, string excludeId, string? localName = null, CancellationToken cancellationToken = default) =>
        TryTarget(targetName, npcId, out var name, out var npc)
            ? LocalDpsCoreAsync(ComparedAttempt("e"), name, npc, excludeId, localName, cancellationToken)
            : Task.FromResult<IReadOnlyList<double>>([]);

    private async Task<IReadOnlyList<double>> LocalDpsCoreAsync(
        string target, string? targetName, long? npcId, string excludeId, string? localName, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var dps = await connection.QueryAsync<double>(
            $"""
            SELECT a.dps FROM encounter_actors a JOIN encounters e ON e.id = a.encounter_id
            WHERE a.is_local = 1 AND {target} AND e.id <> @excludeId AND (@localName IS NULL OR a.name = @localName)
            """,
            new { targetName, npcId, excludeId, localName }).ConfigureAwait(false);
        return [.. dps];
    }

    /// <summary>
    /// Ids de tu intento anterior y de tu mejor intento contra ese objetivo, sin contar <paramref name="excludeId"/>,
    /// para compararlos con la pelea actual. Cada uno es <c>null</c> si no hay. Compara solo por el nombre guardado.
    /// </summary>
    public Task<(string? Previous, string? Best)> PastAttemptsAsync(string targetName, string excludeId, string? localName = null, CancellationToken cancellationToken = default) =>
        PastAttemptsCoreAsync(NameIs, targetName, null, excludeId, localName, cancellationToken);

    /// <summary>
    /// Ids de tu intento de boss anterior y de tu mejor intento de boss (<see cref="BossAttemptOf"/>) contra ese objetivo con la regla de
    /// <see cref="FightTarget.Identity(string?, uint?)"/>. Los dos <c>null</c> si el objetivo no tiene nombre ni plantilla.
    /// </summary>
    public Task<(string? Previous, string? Best)> PastAttemptsAsync(string? targetName, uint? npcId, string excludeId, string? localName = null, CancellationToken cancellationToken = default) =>
        TryTarget(targetName, npcId, out var name, out var npc)
            ? PastAttemptsCoreAsync(ComparedAttempt("e"), name, npc, excludeId, localName, cancellationToken)
            : Task.FromResult<(string? Previous, string? Best)>((null, null));

    private async Task<(string? Previous, string? Best)> PastAttemptsCoreAsync(
        string target, string? targetName, long? npcId, string excludeId, string? localName, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var parameters = new { targetName, npcId, excludeId, localName };
        var previous = await connection.ExecuteScalarAsync<string?>(
            $"""
            SELECT e.id FROM encounter_actors a JOIN encounters e ON e.id = a.encounter_id
            WHERE a.is_local = 1 AND {target} AND e.id <> @excludeId AND (@localName IS NULL OR a.name = @localName)
              AND e.started_at < COALESCE((SELECT started_at FROM encounters WHERE id = @excludeId), '9999')
            ORDER BY e.started_at DESC LIMIT 1
            """,
            parameters).ConfigureAwait(false);
        var best = await connection.ExecuteScalarAsync<string?>(
            $"""
            SELECT e.id FROM encounter_actors a JOIN encounters e ON e.id = a.encounter_id
            WHERE a.is_local = 1 AND {target} AND e.id <> @excludeId AND (@localName IS NULL OR a.name = @localName)
            ORDER BY a.dps DESC LIMIT 1
            """,
            parameters).ConfigureAwait(false);
        return (previous, best);
    }

    /// <summary>
    /// Cuántas peleas guardadas contra ese objetivo empezaron antes de <paramref name="before"/>, sin contar <paramref name="excludeId"/>:
    /// el overlay muestra "Intento N" (N = esto + 1) mientras la pelea todavía no se guardó. Por personaje, como el intento del historial.
    /// Compara solo por el nombre guardado.
    /// </summary>
    public Task<int> CountEarlierAttemptsAsync(string targetName, DateTimeOffset before, string excludeId, string? localName = null, CancellationToken cancellationToken = default) =>
        CountEarlierAttemptsCoreAsync("o.target_name = @targetName", targetName, null, before, excludeId, localName, cancellationToken);

    /// <summary>
    /// El mismo conteo con la regla de <see cref="FightTarget.Identity(string?, uint?)"/>, la del "intento N" del historial:
    /// así el overlay y el historial dan el mismo número. 0 si el objetivo no tiene nombre ni plantilla.
    /// </summary>
    public Task<int> CountEarlierAttemptsAsync(
        string? targetName, uint? npcId, DateTimeOffset before, string excludeId, string? localName = null, CancellationToken cancellationToken = default) =>
        TryTarget(targetName, npcId, out var name, out var npc)
            ? CountEarlierAttemptsCoreAsync(TargetIs("o"), name, npc, before, excludeId, localName, cancellationToken)
            : Task.FromResult(0);

    private async Task<int> CountEarlierAttemptsCoreAsync(
        string target, string? targetName, long? npcId, DateTimeOffset before, string excludeId, string? localName, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var count = await connection.ExecuteScalarAsync<long>(
            $"""
            WITH loc AS (SELECT encounter_id, MAX(name) AS name FROM encounter_actors WHERE is_local = 1 GROUP BY encounter_id)
            SELECT COUNT(*) FROM encounters o LEFT JOIN loc l ON l.encounter_id = o.id
            WHERE {target} AND o.started_at < @before AND o.id <> @excludeId AND l.name IS @localName
            """,
            new { targetName, npcId, before = before.ToString("O", CultureInfo.InvariantCulture), excludeId, localName }).ConfigureAwait(false);
        return (int)count;
    }

    /// <summary>Guarda (o borra, con texto vacío) la nota de una pelea.</summary>
    public async Task SetNoteAsync(string id, string? note, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            "UPDATE encounters SET note = @note WHERE id = @id",
            new { id, note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() }).ConfigureAwait(false);
    }

    public async Task<string?> GetNoteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<string?>("SELECT note FROM encounters WHERE id = @id", new { id }).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync("DELETE FROM encounters WHERE id = @id", new { id }).ConfigureAwait(false);
    }

    /// <summary>
    /// Limpieza: borra las peleas anteriores a <paramref name="before"/>. Las de objetivo derrotado y las que tienen nota
    /// se conservan si <paramref name="keepKillsAndNotes"/> es <c>true</c>. Devuelve cuántas borró.
    /// </summary>
    public async Task<int> DeleteOlderThanAsync(DateTimeOffset before, bool keepKillsAndNotes = true, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteAsync(
            """
            DELETE FROM encounters
            WHERE started_at < @before AND (@keep = 0 OR (target_killed = 0 AND note IS NULL))
            """,
            new { before = before.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), keep = keepKillsAndNotes }).ConfigureAwait(false);
    }

    /// <summary>Encuentro completo, con jugadores y skills, para volver a verlo en el overlay. <c>null</c> si no existe.</summary>
    public async Task<EncounterSnapshot?> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var encounter = await connection.QuerySingleOrDefaultAsync<EncounterDetailRow>(
            """
            SELECT id AS Id, started_at AS StartedAt, ended_at AS EndedAt, target_name AS TargetName,
                   target_killed AS TargetKilled, total_damage AS TotalDamage, main_target_seconds AS MainTargetSeconds,
                   deaths_json AS DeathsJson, zone_entered_at AS ZoneEnteredAt, marks_json AS MarksJson, phases_json AS PhasesJson,
                   target_npc_id AS TargetNpcId
            FROM encounters WHERE id = @id
            """,
            new { id }).ConfigureAwait(false);
        if (encounter is null)
        {
            return null;
        }

        var actors = await connection.QueryAsync<ActorRow>(
            """
            SELECT actor_id AS ActorId, name AS Name, class_id AS ClassId, is_local AS IsLocal, damage AS Damage, dps AS Dps,
                   hits AS Hits, crits AS Crits, max_hit AS MaxHit, healing AS Healing, skills_json AS SkillsJson,
                   main_target_damage AS MainTargetDamage, main_target_dps AS MainTargetDps, periodic_damage AS PeriodicDamage,
                   active_dps AS ActiveDps, counts_json AS CountsJson, overkill AS Overkill,
                   timeline_json AS TimelineJson, damage_taken AS DamageTaken, hits_taken AS HitsTaken, deaths AS Deaths,
                   item_level AS ItemLevel, combat_power AS CombatPower
            FROM encounter_actors WHERE encounter_id = @id ORDER BY damage DESC
            """,
            new { id }).ConfigureAwait(false);

        var total = encounter.TotalDamage;
        return new EncounterSnapshot(
            Guid.Parse(encounter.Id),
            DateTimeOffset.Parse(encounter.StartedAt, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(encounter.EndedAt, CultureInfo.InvariantCulture),
            encounter.TargetName,
            encounter.TargetKilled != 0,
            total,
            [.. actors.Select(a => new ActorSnapshot(
                (ulong)a.ActorId,
                a.Name,
                a.ClassId is { } classId ? (int)classId : null,
                a.IsLocal != 0,
                a.Damage,
                a.Dps,
                total == 0 ? 0 : (double)a.Damage / total,
                (int)a.Hits,
                (int)a.Crits,
                a.MaxHit,
                a.Healing,
                JsonSerializer.Deserialize<SkillSnapshot[]>(a.SkillsJson) ?? [],
                a.CountsJson is null ? null : JsonSerializer.Deserialize<HitCounts>(a.CountsJson),
                a.MainTargetDamage,
                a.MainTargetDps,
                a.PeriodicDamage,
                a.ActiveDps,
                a.Overkill,
                a.TimelineJson is null ? null : JsonSerializer.Deserialize<long[]>(a.TimelineJson),
                a.DamageTaken,
                (int)a.HitsTaken,
                (int)a.Deaths,
                a.ItemLevel is { } itemLevel ? (int)itemLevel : null,
                a.CombatPower))],
            encounter.MainTargetSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            Deaths: encounter.DeathsJson is null ? null : JsonSerializer.Deserialize<DeathRecap[]>(encounter.DeathsJson),
            TargetNpcId: encounter.TargetNpcId is { } npcId ? (uint)npcId : null,
            Marks: encounter.MarksJson is null ? null : JsonSerializer.Deserialize<int[]>(encounter.MarksJson),
            Phases: encounter.PhasesJson is null ? null : JsonSerializer.Deserialize<HealthPhase[]>(encounter.PhasesJson),
            ZoneEnteredAt: encounter.ZoneEnteredAt is null ? null : DateTimeOffset.Parse(encounter.ZoneEnteredAt, CultureInfo.InvariantCulture));
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;").ConfigureAwait(false);
        return connection;
    }
}

/// <param name="LocalDps">DPS del jugador local en esa pelea, si estuvo.</param>
/// <param name="Attempt">Número de intento contra ese objetivo (1 = la primera vez); 0 sin nombre ni plantilla de NPC.</param>
/// <param name="IsRecord">Es el mejor DPS propio contra ese objetivo.</param>
/// <param name="MainTargetSeconds">Cuánto aguantó el objetivo principal, si se midió.</param>
/// <param name="ActorsDamage">Daño de toda la party, a cualquier objetivo.</param>
/// <param name="MainTargetDamage">Daño de toda la party al objetivo principal.</param>
/// <param name="Deaths">Muertes de toda la party en la pelea; <c>null</c> si no tiene jugadores guardados.</param>
/// <param name="TargetNpcId">Plantilla del objetivo principal; <c>null</c> en las peleas guardadas antes de que se registrara.</param>
public sealed record EncounterSummary(
    string Id,
    string StartedAt,
    string EndedAt,
    string? TargetName,
    bool TargetKilled,
    long TotalDamage,
    string? Note = null,
    double? LocalDps = null,
    int Attempt = 0,
    bool IsRecord = false,
    string? ZoneEnteredAt = null,
    string? LocalName = null,
    double? MainTargetSeconds = null,
    long ActorsDamage = 0,
    long MainTargetDamage = 0,
    int? Deaths = null,
    uint? TargetNpcId = null);

/// <summary>SQLite no tiene booleanos: la fila cruda trae <c>target_killed</c> como entero.</summary>
/// <summary>
/// Con propiedades y no con constructor: Sqlite informa como BLOB una columna calculada que en la primera fila viene NULL
/// (por ejemplo el récord de una pelea sin objetivo) y Dapper no encontraba un constructor con esos tipos.
/// </summary>
internal sealed class EncounterRow
{
    public string Id { get; set; } = "";
    public string StartedAt { get; set; } = "";
    public string EndedAt { get; set; } = "";
    public string? TargetName { get; set; }
    public long TargetKilled { get; set; }
    public long TotalDamage { get; set; }
    public string? Note { get; set; }
    public double? LocalDps { get; set; }
    public double? BestLocalDps { get; set; }
    public long Attempt { get; set; }
    public string? ZoneEnteredAt { get; set; }
    public string? LocalName { get; set; }
    public double? MainTargetSeconds { get; set; }
    public long? ActorsDamage { get; set; }
    public long? MainTargetDamage { get; set; }
    public long? Deaths { get; set; }
    public long? TargetNpcId { get; set; }
}

internal sealed record EncounterDetailRow(
    string Id, string StartedAt, string EndedAt, string? TargetName, long TargetKilled, long TotalDamage, double? MainTargetSeconds, string? DeathsJson,
    string? ZoneEnteredAt, string? MarksJson, string? PhasesJson, long? TargetNpcId);

internal sealed record ActorRow(
    long ActorId, string Name, long? ClassId, long IsLocal, long Damage, double Dps, long Hits, long Crits, long MaxHit, long Healing,
    string SkillsJson, long MainTargetDamage, double MainTargetDps, long PeriodicDamage, double ActiveDps, string? CountsJson, long Overkill, string? TimelineJson,
    long DamageTaken, long HitsTaken, long Deaths, long? ItemLevel, long? CombatPower);
