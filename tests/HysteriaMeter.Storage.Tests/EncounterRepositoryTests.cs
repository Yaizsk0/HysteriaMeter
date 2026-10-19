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
                    counts, 648_468, 23_458, 70_000, 24_000, Overkill: 3_500, Timeline: [100, 0, 250], DamageTaken: 12_000, HitsTaken: 7, Deaths: 1, ItemLevel: 2130, CombatPower: 93_100),
            ],
            TimeSpan.FromSeconds(27.64),
            Deaths:
            [
                new DeathRecap(start.AddSeconds(20), 1879, "Griffith",
                    [new CombatLogEntry(start.AddSeconds(19), LogKind.Damage, 900, "Tarka", 1879, "Griffith", 502, 9_000, HitModifiers.Critical)]),
            ],
            Marks: [12, 20],
            ZoneEnteredAt: start.AddMinutes(-5),
            Phases: [new HealthPhase(75, 9), new HealthPhase(50, 18)]);
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
        Assert.Equal([12, 20], loaded.Marks!);
        Assert.Equal(snapshot.Phases, loaded.Phases!);
        Assert.Equal(start.AddMinutes(-5), loaded.ZoneEnteredAt);
        Assert.Equal(start.AddMinutes(-5).ToString("O", System.Globalization.CultureInfo.InvariantCulture), Assert.Single(await repository.ListRecentAsync()).ZoneEnteredAt);
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
    public async Task Damage_taken_by_skill_survives_a_save_and_load_round_trip()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        var start = new DateTimeOffset(2026, 10, 18, 21, 0, 0, TimeSpan.Zero);
        // El local recibió de dos skills del boss; el aliado no recibió nada y queda sin desglose (NULL).
        var fight = new EncounterSnapshot(
            Guid.NewGuid(), start, start.AddSeconds(30), "Boss", TargetKilled: true, TotalDamage: 1_500,
            [
                new ActorSnapshot(1, "Local", null, true, 1_000, 33, 0.66, 4, 0, 400, 0, [], DamageTaken: 1_600, HitsTaken: 4,
                    DamageTakenBySkill: [new TakenSkillSnapshot(501, 1_000, 1), new TakenSkillSnapshot(500, 600, 3)]),
                new ActorSnapshot(2, "Aliado", 15, false, 500, 16, 0.33, 2, 0, 300, 0, []),
            ]);
        await repository.SaveAsync(fight);

        var loaded = (await repository.LoadAsync(fight.Id.ToString()))!.Actors.ToDictionary(a => a.Id);

        Assert.Equal(fight.Actors[0].DamageTakenBySkill!, loaded[1].DamageTakenBySkill!);
        Assert.Equal((1_600L, 4), (loaded[1].DamageTaken, loaded[1].HitsTaken));
        Assert.Null(loaded[2].DamageTakenBySkill);
    }

    [Fact]
    public async Task Old_fights_without_the_breakdown_load_with_their_damage_taken()
    {
        // Una base de antes de damage_taken_json, con una pelea en la que el local ya había recibido daño.
        var oldId = Guid.NewGuid().ToString();
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE TABLE encounters (id TEXT PRIMARY KEY, started_at TEXT NOT NULL, ended_at TEXT NOT NULL,
                    target_name TEXT, target_killed INTEGER NOT NULL, total_damage INTEGER NOT NULL);
                CREATE TABLE encounter_actors (encounter_id TEXT NOT NULL, actor_id INTEGER NOT NULL, name TEXT NOT NULL,
                    class_id INTEGER, is_local INTEGER NOT NULL, damage INTEGER NOT NULL, dps REAL NOT NULL, hits INTEGER NOT NULL,
                    crits INTEGER NOT NULL, max_hit INTEGER NOT NULL, healing INTEGER NOT NULL, skills_json TEXT NOT NULL,
                    damage_taken INTEGER NOT NULL DEFAULT 0, hits_taken INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (encounter_id, actor_id));
                INSERT INTO encounters VALUES ('{oldId}', '2026-10-16T20:00:00.0000000+00:00', '2026-10-16T20:00:03.0000000+00:00', 'Boss', 1, 350);
                INSERT INTO encounter_actors VALUES ('{oldId}', 1, 'Local', NULL, 1, 350, 116, 2, 0, 250, 0, '[]', 900, 3);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();

        var actor = Assert.Single((await repository.LoadAsync(oldId))!.Actors);
        Assert.Equal((900L, 3), (actor.DamageTaken, actor.HitsTaken));
        Assert.Null(actor.DamageTakenBySkill);
    }

    [Fact]
    public async Task Target_verification_survives_a_save_and_load_round_trip()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        var start = new DateTimeOffset(2026, 10, 18, 21, 0, 0, TimeSpan.Zero);
        // El boss cierra con overkill; al add le falta daño.
        var fight = new EncounterSnapshot(
            Guid.NewGuid(), start, start.AddSeconds(30), "Boss", TargetKilled: true, TotalDamage: 16_500,
            [new ActorSnapshot(1, "Local", null, true, 16_500, 550, 1, 6, 0, 3_000, 0, [])],
            Verifications:
            [
                new TargetVerification(900, 12345, 10_000, 0, 10_000, 10_500, 500, 0, true),
                new TargetVerification(901, null, 8_000, 2_000, 8_000, 4_000, 0, -2_000, false),
            ]);
        await repository.SaveAsync(fight);

        var loaded = await repository.LoadAsync(fight.Id.ToString());

        Assert.Equal(fight.Verifications, loaded!.Verifications!);
    }

    [Fact]
    public async Task Old_fights_without_verification_load_without_it()
    {
        // Una base de antes de verification_json, con una pelea guardada: carga sin verificación.
        var oldId = Guid.NewGuid().ToString();
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = $$"""
                CREATE TABLE encounters (id TEXT PRIMARY KEY, started_at TEXT NOT NULL, ended_at TEXT NOT NULL,
                    target_name TEXT, target_killed INTEGER NOT NULL, total_damage INTEGER NOT NULL, phases_json TEXT);
                CREATE TABLE encounter_actors (encounter_id TEXT NOT NULL, actor_id INTEGER NOT NULL, name TEXT NOT NULL,
                    class_id INTEGER, is_local INTEGER NOT NULL, damage INTEGER NOT NULL, dps REAL NOT NULL, hits INTEGER NOT NULL,
                    crits INTEGER NOT NULL, max_hit INTEGER NOT NULL, healing INTEGER NOT NULL, skills_json TEXT NOT NULL,
                    PRIMARY KEY (encounter_id, actor_id));
                INSERT INTO encounters VALUES ('{{oldId}}', '2026-10-16T20:00:00.0000000+00:00', '2026-10-16T20:00:03.0000000+00:00', 'Boss', 1, 350,
                    '[{"Percent":75,"Second":1}]');
                INSERT INTO encounter_actors VALUES ('{{oldId}}', 1, 'Local', NULL, 1, 350, 116, 2, 0, 250, 0, '[]');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('encounters') WHERE name = 'verification_json'";
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }

        var loaded = await repository.LoadAsync(oldId);
        Assert.Equal([new HealthPhase(75, 1)], loaded!.Phases!);
        Assert.Null(loaded.Verifications);
    }

    [Fact]
    public async Task Lists_when_the_latest_fight_has_no_target_name()
    {
        // La pelea más reciente sin nombre de objetivo (monstruo sin npcs.json): el récord sale NULL en la primera
        // fila y SQLite no le da tipo a la columna; el historial se caía al abrirse.
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        var start = new DateTimeOffset(2026, 10, 16, 4, 0, 0, TimeSpan.Zero);
        await repository.SaveAsync(Fight(start, "Tarka", localDps: 1000));
        await repository.SaveAsync(new EncounterSnapshot(
            Guid.NewGuid(), start.AddMinutes(5), start.AddMinutes(6), null, TargetKilled: true, TotalDamage: 500,
            [new ActorSnapshot(2, "Rubia", 15, false, 500, 50, 1, 5, 0, 200, 0, [])]));

        var list = await repository.ListRecentAsync();

        Assert.Equal(2, list.Count);
        Assert.Null(list[0].LocalDps);
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
        Assert.Equal(1500, await repository.PreviousLocalDpsAsync("Tarka", third.Id.ToString()));
        Assert.Equal(1000, await repository.PreviousLocalDpsAsync("Tarka", second.Id.ToString()));
        Assert.Null(await repository.PreviousLocalDpsAsync("Otro", other.Id.ToString()));
        Assert.Equal((second.Id.ToString(), second.Id.ToString()), await repository.PastAttemptsAsync("Tarka", third.Id.ToString()));
        Assert.Equal((first.Id.ToString(), third.Id.ToString()), await repository.PastAttemptsAsync("Tarka", second.Id.ToString()));
        Assert.Equal(((string?)null, (string?)null), await repository.PastAttemptsAsync("Otro", other.Id.ToString()));
    }

    [Fact]
    public async Task Records_and_attempts_are_per_character()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        var start = new DateTimeOffset(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);
        var main = Fight(start, "Tarka", localDps: 3000, localName: "Tests");
        var alt = Fight(start.AddMinutes(10), "Tarka", localDps: 1000, localName: "Alt");
        var altAgain = Fight(start.AddMinutes(20), "Tarka", localDps: 1200, localName: "Alt");
        foreach (var fight in new[] { main, alt, altAgain })
        {
            await repository.SaveAsync(fight);
        }

        var list = (await repository.ListRecentAsync()).ToDictionary(e => e.Id);
        Assert.True(list[altAgain.Id.ToString()].IsRecord);
        Assert.Equal(2, list[altAgain.Id.ToString()].Attempt);
        Assert.Equal("Alt", list[altAgain.Id.ToString()].LocalName);
        Assert.Equal(1000, await repository.BestLocalDpsAsync("Tarka", altAgain.Id.ToString(), "Alt"));
        Assert.Equal(3000, await repository.BestLocalDpsAsync("Tarka", altAgain.Id.ToString()));
        Assert.Null(await repository.PreviousLocalDpsAsync("Tarka", main.Id.ToString(), "Tests"));
        Assert.Equal((alt.Id.ToString(), alt.Id.ToString()), await repository.PastAttemptsAsync("Tarka", altAgain.Id.ToString(), "Alt"));
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

    /// <summary>Un wipe de 60 s con todo el daño al objetivo: un intento de boss, que es lo que comparan el récord y el anterior.</summary>
    private static EncounterSnapshot Fight(DateTimeOffset start, string target, double localDps, string localName = "Mati") => new(
        Guid.NewGuid(),
        start,
        start.AddSeconds(60),
        target,
        TargetKilled: false,
        TotalDamage: (long)(localDps * 60),
        [new ActorSnapshot(1, localName, null, true, (long)(localDps * 60), localDps, 1, 10, 2, 100, 0, [], MainTargetDamage: (long)(localDps * 60))]);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }
}
