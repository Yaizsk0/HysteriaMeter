using HysteriaMeter.Combat;

namespace HysteriaMeter.Storage.Tests;

public sealed class EncounterRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"hysteria-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Saves_and_lists_encounters()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();

        var start = new DateTimeOffset(2026, 10, 14, 21, 0, 0, TimeSpan.Zero);
        var snapshot = new EncounterSnapshot(
            Guid.NewGuid(),
            start,
            start.AddSeconds(30),
            "Boss",
            TargetKilled: true,
            TotalDamage: 3000,
            [
                new ActorSnapshot(1, "Mati", 3, true, 3000, 100, 1, 10, 4, 900, 0, [new SkillSnapshot(10, 3000, 10, 4, 900, 1)]),
            ]);

        await repository.SaveAsync(snapshot);
        var recent = await repository.ListRecentAsync();

        var saved = Assert.Single(recent);
        Assert.Equal(snapshot.Id.ToString(), saved.Id);
        Assert.Equal("Boss", saved.TargetName);
        Assert.True(saved.TargetKilled);
        Assert.Equal(3000, saved.TotalDamage);
    }

    [Fact]
    public async Task Migrates_a_database_created_by_the_first_version()
    {
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE encounters (id TEXT PRIMARY KEY, started_at TEXT NOT NULL, ended_at TEXT NOT NULL,
                    target_name TEXT, target_killed INTEGER NOT NULL, total_damage INTEGER NOT NULL);
                CREATE TABLE encounter_actors (encounter_id TEXT NOT NULL, actor_id INTEGER NOT NULL, name TEXT NOT NULL,
                    class_id INTEGER, is_local INTEGER NOT NULL, damage INTEGER NOT NULL, dps REAL NOT NULL, hits INTEGER NOT NULL,
                    crits INTEGER NOT NULL, max_hit INTEGER NOT NULL, healing INTEGER NOT NULL, skills_json TEXT NOT NULL,
                    PRIMARY KEY (encounter_id, actor_id));
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        await repository.InitializeAsync(); // idempotente

        var start = new DateTimeOffset(2026, 10, 15, 13, 39, 40, TimeSpan.Zero);
        await repository.SaveAsync(new EncounterSnapshot(
            Guid.NewGuid(),
            start,
            start.AddSeconds(28),
            "Destroyer Tarka",
            TargetKilled: true,
            TotalDamage: 654_021,
            [
                new ActorSnapshot(1879, "Griffith", null, true, 654_021, 23_659, 1, 98, 51, 90_000, 0, [],
                    new HitCounts(92, 51, 5, 5, 62, 25, 23), 648_468, 23_458, 70_000, 24_000),
            ],
            TimeSpan.FromSeconds(27.64)));

        Assert.Single(await repository.ListRecentAsync());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }
}
