using HysteriaMeter.Combat;
using Microsoft.Data.Sqlite;

namespace HysteriaMeter.Storage.Tests;

/// <summary>
/// La serie de daño por segundo sin DoT (direct_timeline_json) se guarda y se lee. Las bases viejas reciben la columna
/// vacía: sus peleas cargan sin la serie y la app usa el Timeline, como antes.
/// </summary>
public sealed class DirectTimelineTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 21, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"hysteria-direct-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Old_fights_load_without_the_direct_series_and_keep_their_timeline()
    {
        // Una base de antes de la columna, con una pelea que ya tenía timeline_json.
        var oldId = Guid.NewGuid().ToString();
        await using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE TABLE encounters (id TEXT PRIMARY KEY, started_at TEXT NOT NULL, ended_at TEXT NOT NULL,
                    target_name TEXT, target_killed INTEGER NOT NULL, total_damage INTEGER NOT NULL);
                CREATE TABLE encounter_actors (encounter_id TEXT NOT NULL, actor_id INTEGER NOT NULL, name TEXT NOT NULL,
                    class_id INTEGER, is_local INTEGER NOT NULL, damage INTEGER NOT NULL, dps REAL NOT NULL, hits INTEGER NOT NULL,
                    crits INTEGER NOT NULL, max_hit INTEGER NOT NULL, healing INTEGER NOT NULL, skills_json TEXT NOT NULL,
                    timeline_json TEXT, PRIMARY KEY (encounter_id, actor_id));
                INSERT INTO encounters VALUES ('{oldId}', '2026-10-16T20:00:00.0000000+00:00', '2026-10-16T20:00:03.0000000+00:00', 'Kromede', 1, 350);
                INSERT INTO encounter_actors VALUES ('{oldId}', 1, 'Mati', NULL, 1, 350, 116, 2, 0, 250, 0, '[]', '[100,0,250]');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();

        await using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('encounter_actors') WHERE name = 'direct_timeline_json'";
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }

        var actor = Assert.Single((await repository.LoadAsync(oldId))!.Actors);
        Assert.Equal([100L, 0, 250], actor.Timeline);
        Assert.Null(actor.DirectTimeline);
    }

    [Fact]
    public async Task The_direct_series_survives_a_save_and_load_round_trip()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        // Mati: un tick de DoT en el segundo 1. Rubia, sin DoT: sin la serie (NULL), igual que una pelea vieja.
        var fight = new EncounterSnapshot(
            Guid.NewGuid(), Start, Start.AddSeconds(3), "Kromede", TargetKilled: true, TotalDamage: 1_050,
            [
                new ActorSnapshot(1, "Mati", null, true, 650, 216, 0.62, 2, 0, 250, 0, [], PeriodicDamage: 300,
                    Timeline: [100, 300, 250], DirectTimeline: [100, 0, 250]),
                new ActorSnapshot(2, "Rubia", 15, false, 400, 133, 0.38, 2, 0, 200, 0, [], Timeline: [200, 0, 200]),
            ]);
        await repository.SaveAsync(fight);

        var loaded = (await repository.LoadAsync(fight.Id.ToString()))!.Actors.ToDictionary(a => a.Id);

        Assert.Equal([100L, 0, 250], loaded[1].DirectTimeline);
        Assert.Equal([100L, 300, 250], loaded[1].Timeline);
        Assert.Null(loaded[2].DirectTimeline);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }
}
