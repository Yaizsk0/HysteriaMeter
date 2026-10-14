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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
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
    }

    public async Task SaveAsync(EncounterSnapshot encounter, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            """
            INSERT OR REPLACE INTO encounters (id, started_at, ended_at, target_name, target_killed, total_damage)
            VALUES (@Id, @Start, @End, @TargetName, @TargetKilled, @TotalDamage)
            """,
            new
            {
                Id = encounter.Id.ToString(),
                Start = encounter.Start.ToString("O", CultureInfo.InvariantCulture),
                End = encounter.End.ToString("O", CultureInfo.InvariantCulture),
                encounter.TargetName,
                encounter.TargetKilled,
                encounter.TotalDamage,
            },
            transaction).ConfigureAwait(false);

        await connection.ExecuteAsync(
            """
            INSERT OR REPLACE INTO encounter_actors
                (encounter_id, actor_id, name, class_id, is_local, damage, dps, hits, crits, max_hit, healing, skills_json)
            VALUES (@EncounterId, @ActorId, @Name, @ClassId, @IsLocal, @Damage, @Dps, @Hits, @Crits, @MaxHit, @Healing, @SkillsJson)
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
            }),
            transaction).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EncounterSummary>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<EncounterRow>(
            """
            SELECT id AS Id, started_at AS StartedAt, ended_at AS EndedAt, target_name AS TargetName,
                   target_killed AS TargetKilled, total_damage AS TotalDamage
            FROM encounters ORDER BY started_at DESC LIMIT @limit
            """,
            new { limit }).ConfigureAwait(false);
        return [.. rows.Select(r => new EncounterSummary(r.Id, r.StartedAt, r.EndedAt, r.TargetName, r.TargetKilled != 0, r.TotalDamage))];
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;").ConfigureAwait(false);
        return connection;
    }
}

public sealed record EncounterSummary(string Id, string StartedAt, string EndedAt, string? TargetName, bool TargetKilled, long TotalDamage);

/// <summary>SQLite no tiene booleanos: la fila cruda trae <c>target_killed</c> como entero.</summary>
internal sealed record EncounterRow(string Id, string StartedAt, string EndedAt, string? TargetName, long TargetKilled, long TotalDamage);
