using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Events;

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
    public async Task Loads_a_saved_encounter_back()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();

        var start = new DateTimeOffset(2026, 10, 15, 13, 39, 40, TimeSpan.Zero);
        var counts = new HitCounts(92, 51, 5, 5, 62, 25, 23);
        var snapshot = new EncounterSnapshot(
            Guid.NewGuid(),
            start,
            start.AddSeconds(28),
            "Destroyer Tarka",
            TargetKilled: true,
            TotalDamage: 654_021,
            [
                new ActorSnapshot(1879, "Griffith", null, true, 654_021, 23_659, 1, 98, 51, 90_000, 1_200,
                    [new SkillSnapshot(10, 654_021, 98, 51, 90_000, 1, counts, 70_000)],
                    counts, 648_468, 23_458, 70_000, 24_000, Overkill: 3_500, Timeline: [100, 0, 250], DamageTaken: 12_000, HitsTaken: 7, Deaths: 1),
            ],
            TimeSpan.FromSeconds(27.64),
            Deaths:
            [
                new DeathRecap(start.AddSeconds(20), 1879, "Griffith",
                    [new CombatLogEntry(start.AddSeconds(19), LogKind.Damage, 900, "Tarka", 1879, "Griffith", 502, 9_000, HitModifiers.Critical)]),
            ]);
        await repository.SaveAsync(snapshot);

        var loaded = await repository.LoadAsync(snapshot.Id.ToString());

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.Start, loaded.Start);
        Assert.Equal(snapshot.Duration, loaded.Duration);
        Assert.Equal(TimeSpan.FromSeconds(27.64), loaded.MainTargetDuration);
        var actor = Assert.Single(loaded.Actors);
        Assert.Equal(snapshot.Actors[0] with { Skills = [], Timeline = null }, actor with { Skills = [], Timeline = null });
        Assert.Equal(snapshot.Actors[0].Timeline, actor.Timeline);
        Assert.Equal(snapshot.Actors[0].Skills, actor.Skills);
        var death = Assert.Single(loaded.Deaths!);
        Assert.Equal(snapshot.Deaths![0] with { LastHits = [] }, death with { LastHits = [] });
        Assert.Equal(snapshot.Deaths[0].LastHits, death.LastHits);
        Assert.Null(await repository.LoadAsync(Guid.NewGuid().ToString()));
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

    [Fact]
    public async Task Lists_attempts_records_and_notes_per_target()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        var start = new DateTimeOffset(2026, 10, 15, 21, 0, 0, TimeSpan.Zero);

        var first = Fight(start, "Tarka", localDps: 1000);
        var second = Fight(start.AddMinutes(10), "Tarka", localDps: 1500);
        var third = Fight(start.AddMinutes(20), "Tarka", localDps: 1200);
        var other = Fight(start.AddMinutes(30), "Otro", localDps: 50);
        foreach (var fight in new[] { first, second, third, other })
        {
            await repository.SaveAsync(fight);
        }

        await repository.SetNoteAsync(third.Id.ToString(), "  con piedra nueva ");
        var list = (await repository.ListRecentAsync()).ToDictionary(e => e.Id);

        Assert.Equal(3, list[third.Id.ToString()].Attempt);
        Assert.Equal(1, list[other.Id.ToString()].Attempt);
        Assert.True(list[second.Id.ToString()].IsRecord);
        Assert.False(list[third.Id.ToString()].IsRecord);
        Assert.Equal(1200, list[third.Id.ToString()].LocalDps);
        Assert.Equal("con piedra nueva", list[third.Id.ToString()].Note);
        Assert.Equal(1500, await repository.BestLocalDpsAsync("Tarka", third.Id.ToString()));
        Assert.Null(await repository.BestLocalDpsAsync("Otro", other.Id.ToString()));
    }

    [Fact]
    public async Task Cleanup_keeps_kills_and_notes()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        var start = new DateTimeOffset(2026, 9, 1, 21, 0, 0, TimeSpan.Zero);

        var trash = Fight(start, "Trash", localDps: 10);
        var kill = Fight(start.AddMinutes(1), "Boss", localDps: 10) with { TargetKilled = true };
        var noted = Fight(start.AddMinutes(2), "Trash", localDps: 10);
        var recent = Fight(start.AddDays(40), "Trash", localDps: 10);
        foreach (var fight in new[] { trash, kill, noted, recent })
        {
            await repository.SaveAsync(fight);
        }

        await repository.SetNoteAsync(noted.Id.ToString(), "importante");

        Assert.Equal(1, await repository.DeleteOlderThanAsync(start.AddDays(30)));
        Assert.Equal(3, (await repository.ListRecentAsync()).Count);

        await repository.DeleteAsync(kill.Id.ToString());
        Assert.Null(await repository.LoadAsync(kill.Id.ToString()));
    }

    private static EncounterSnapshot Fight(DateTimeOffset start, string target, double localDps) => new(
        Guid.NewGuid(),
        start,
        start.AddSeconds(60),
        target,
        TargetKilled: false,
        TotalDamage: (long)(localDps * 60),
        [new ActorSnapshot(1, "Mati", null, true, (long)(localDps * 60), localDps, 1, 10, 2, 100, 0, [])]);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }
}
