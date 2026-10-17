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
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_encounters_target ON encounters(target_name, started_at)").ConfigureAwait(false);
    }

    private static async Task AddColumnIfMissingAsync(SqliteConnection connection, string table, string column, string definition)
    {
        var columns = await connection.QueryAsync<string>($"SELECT name FROM pragma_table_info('{table}')").ConfigureAwait(false);
        if (!columns.Contains(column, StringComparer.Ordinal))
        {
            await connection.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}").ConfigureAwait(false);
        }
    }

    public async Task SaveAsync(EncounterSnapshot encounter, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            """
            INSERT OR REPLACE INTO encounters (id, started_at, ended_at, target_name, target_killed, total_damage, main_target_seconds, deaths_json,
                                               zone_entered_at, marks_json, phases_json)
            VALUES (@Id, @Start, @End, @TargetName, @TargetKilled, @TotalDamage, @MainTargetSeconds, @DeathsJson, @ZoneEnteredAt, @MarksJson, @PhasesJson)
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
    }

    public async Task<IReadOnlyList<EncounterSummary>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        // Intento: cuántas veces se peleó contra ese objetivo hasta esta pelea. Récord: el mejor DPS propio contra ese objetivo.
        // Los dos son por personaje: con varios personajes, cada uno compite contra sí mismo ("IS" compara también null).
        // Las sumas de toda la party (daño, daño al objetivo principal, muertes) alcanzan para clasificar la pelea con
        // BossRules sin cargar cada una entera.
        var rows = await connection.QueryAsync<EncounterRow>(
            """
            WITH loc AS (SELECT encounter_id, MAX(dps) AS dps, MAX(name) AS name FROM encounter_actors WHERE is_local = 1 GROUP BY encounter_id),
                 party AS (SELECT encounter_id, SUM(damage) AS damage, SUM(main_target_damage) AS main_damage, SUM(deaths) AS deaths
                           FROM encounter_actors GROUP BY encounter_id)
            SELECT e.id AS Id, e.started_at AS StartedAt, e.ended_at AS EndedAt, e.target_name AS TargetName,
                   e.target_killed AS TargetKilled, e.total_damage AS TotalDamage, e.note AS Note,
                   l.dps AS LocalDps,
                   (SELECT MAX(l2.dps) FROM loc l2 JOIN encounters o ON o.id = l2.encounter_id
                     WHERE o.target_name = e.target_name AND l2.name IS l.name) AS BestLocalDps,
                   (SELECT COUNT(*) FROM encounters o LEFT JOIN loc l2 ON l2.encounter_id = o.id
                     WHERE o.target_name = e.target_name AND o.started_at <= e.started_at AND l2.name IS l.name) AS Attempt,
                   e.zone_entered_at AS ZoneEnteredAt,
                   l.name AS LocalName,
                   e.main_target_seconds AS MainTargetSeconds,
                   p.damage AS ActorsDamage, p.main_damage AS MainTargetDamage, p.deaths AS Deaths
            FROM encounters e LEFT JOIN loc l ON l.encounter_id = e.id LEFT JOIN party p ON p.encounter_id = e.id
            ORDER BY e.started_at DESC LIMIT @limit
            """,
            new { limit }).ConfigureAwait(false);
        return [.. rows.Select(r => new EncounterSummary(
            r.Id,
            r.StartedAt,
            r.EndedAt,
            r.TargetName,
            r.TargetKilled != 0,
            r.TotalDamage,
            r.Note,
            r.LocalDps,
            r.TargetName is null ? 0 : (int)r.Attempt,
            r.LocalDps is { } dps && r.BestLocalDps is { } best && dps >= best,
            r.ZoneEnteredAt,
            r.LocalName,
            r.MainTargetSeconds,
            r.ActorsDamage ?? 0,
            r.MainTargetDamage ?? 0,
            r.Deaths is { } deaths ? (int)deaths : null))];
    }

    /// <summary>
    /// Mejor DPS propio contra ese objetivo, sin contar la pelea <paramref name="excludeId"/>. <c>null</c> si no hay otra.
    /// Con <paramref name="localName"/>, solo las peleas con ese personaje.
    /// </summary>
    public async Task<double?> BestLocalDpsAsync(string targetName, string excludeId, string? localName = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<double?>(
            """
            SELECT MAX(a.dps) FROM encounter_actors a JOIN encounters e ON e.id = a.encounter_id
            WHERE a.is_local = 1 AND e.target_name = @targetName AND e.id <> @excludeId AND (@localName IS NULL OR a.name = @localName)
            """,
            new { targetName, excludeId, localName }).ConfigureAwait(false);
    }

    /// <summary>DPS propio en el intento anterior contra ese objetivo (el último antes de <paramref name="excludeId"/>). <c>null</c> si no hay.</summary>
    public async Task<double?> PreviousLocalDpsAsync(string targetName, string excludeId, string? localName = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<double?>(
            """
            SELECT a.dps FROM encounter_actors a JOIN encounters e ON e.id = a.encounter_id
            WHERE a.is_local = 1 AND e.target_name = @targetName AND e.id <> @excludeId AND (@localName IS NULL OR a.name = @localName)
              AND e.started_at < COALESCE((SELECT started_at FROM encounters WHERE id = @excludeId), '9999')
            ORDER BY e.started_at DESC LIMIT 1
            """,
            new { targetName, excludeId, localName }).ConfigureAwait(false);
    }

    /// <summary>
    /// Ids de tu intento anterior y de tu mejor intento contra ese objetivo, sin contar <paramref name="excludeId"/>,
    /// para compararlos con la pelea actual. Cada uno es <c>null</c> si no hay.
    /// </summary>
    public async Task<(string? Previous, string? Best)> PastAttemptsAsync(string targetName, string excludeId, string? localName = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var previous = await connection.ExecuteScalarAsync<string?>(
            """
            SELECT e.id FROM encounter_actors a JOIN encounters e ON e.id = a.encounter_id
            WHERE a.is_local = 1 AND e.target_name = @targetName AND e.id <> @excludeId AND (@localName IS NULL OR a.name = @localName)
              AND e.started_at < COALESCE((SELECT started_at FROM encounters WHERE id = @excludeId), '9999')
            ORDER BY e.started_at DESC LIMIT 1
            """,
            new { targetName, excludeId, localName }).ConfigureAwait(false);
        var best = await connection.ExecuteScalarAsync<string?>(
            """
            SELECT e.id FROM encounter_actors a JOIN encounters e ON e.id = a.encounter_id
            WHERE a.is_local = 1 AND e.target_name = @targetName AND e.id <> @excludeId AND (@localName IS NULL OR a.name = @localName)
            ORDER BY a.dps DESC LIMIT 1
            """,
            new { targetName, excludeId, localName }).ConfigureAwait(false);
        return (previous, best);
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
                   deaths_json AS DeathsJson, zone_entered_at AS ZoneEnteredAt, marks_json AS MarksJson, phases_json AS PhasesJson
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
/// <param name="Attempt">Número de intento contra ese objetivo (1 = la primera vez); 0 sin objetivo con nombre.</param>
/// <param name="IsRecord">Es el mejor DPS propio contra ese objetivo.</param>
/// <param name="MainTargetSeconds">Cuánto aguantó el objetivo principal, si se midió.</param>
/// <param name="ActorsDamage">Daño de toda la party, a cualquier objetivo.</param>
/// <param name="MainTargetDamage">Daño de toda la party al objetivo principal.</param>
/// <param name="Deaths">Muertes de toda la party en la pelea; <c>null</c> si no tiene jugadores guardados.</param>
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
    int? Deaths = null);

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
}

internal sealed record EncounterDetailRow(
    string Id, string StartedAt, string EndedAt, string? TargetName, long TargetKilled, long TotalDamage, double? MainTargetSeconds, string? DeathsJson,
    string? ZoneEnteredAt, string? MarksJson, string? PhasesJson);

internal sealed record ActorRow(
    long ActorId, string Name, long? ClassId, long IsLocal, long Damage, double Dps, long Hits, long Crits, long MaxHit, long Healing,
    string SkillsJson, long MainTargetDamage, double MainTargetDps, long PeriodicDamage, double ActiveDps, string? CountsJson, long Overkill, string? TimelineJson,
    long DamageTaken, long HitsTaken, long Deaths, long? ItemLevel, long? CombatPower);
