using HysteriaMeter.Combat;
using Microsoft.Data.Sqlite;

namespace HysteriaMeter.Storage.Tests;

/// <summary>
/// La plantilla del objetivo (target_npc_id) se guarda y se lee: sin nombre en data/npcs.json, los intentos y el récord
/// se cuentan por plantilla. Las bases viejas reciben la columna vacía y sus peleas siguen sin nada con qué compararse.
/// </summary>
public sealed class TargetNpcIdTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"hysteria-npc-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Migrates_a_database_without_the_column_and_keeps_its_old_fights()
    {
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
                    PRIMARY KEY (encounter_id, actor_id));
                INSERT INTO encounters VALUES ('{oldId}', '2026-10-16T20:00:00.0000000+00:00', '2026-10-16T20:01:30.0000000+00:00', NULL, 1, 9000);
                INSERT INTO encounter_actors VALUES ('{oldId}', 1, 'Mati', NULL, 1, 9000, 100, 10, 2, 900, 0, '[]');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();

        await using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('encounters') WHERE name = 'target_npc_id'";
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }

        var old = await repository.LoadAsync(oldId);
        Assert.NotNull(old);
        Assert.Null(old.TargetNpcId);

        var fresh = Fight(Start, null, 2400123, 120);
        await repository.SaveAsync(fresh);

        var listed = await repository.ListRecentAsync();
        Assert.Equal([(uint?)2400123, null], listed.Select(s => s.TargetNpcId));
        // La vieja no tiene nombre ni plantilla: intento 0 y sin récord, aunque tenga el único DPS de su "grupo".
        Assert.Equal((0, false), (listed[1].Attempt, listed[1].IsRecord));
        Assert.Equal((1, true), (listed[0].Attempt, listed[0].IsRecord));
    }

    [Fact]
    public async Task The_npc_id_survives_a_save_and_load_round_trip()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        var fight = Fight(Start, "Kromede", 2300371, 150);
        await repository.SaveAsync(fight);

        var loaded = await repository.LoadAsync(fight.Id.ToString());
        var listed = Assert.Single(await repository.ListRecentAsync());

        Assert.Equal((uint?)2300371, loaded!.TargetNpcId);
        Assert.Equal((uint?)2300371, listed.TargetNpcId);
        Assert.Equal("Kromede", listed.TargetName);
    }

    [Fact]
    public async Task Unnamed_fights_count_attempts_and_records_by_npc_id()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        await repository.SaveAsync(Fight(Start, null, 111, 100));
        await repository.SaveAsync(Fight(Start.AddMinutes(5), null, 222, 300));
        await repository.SaveAsync(Fight(Start.AddMinutes(10), null, 111, 150));
        await repository.SaveAsync(Fight(Start.AddMinutes(15), null, 111, 120));

        var listed = (await repository.ListRecentAsync()).Reverse().ToList();

        // 111: intentos 1, 2 y 3; el récord es el de 150 aunque 222 haya hecho más (es otro objetivo).
        Assert.Equal([1, 1, 2, 3], listed.Select(s => s.Attempt));
        Assert.Equal([false, true, true, false], listed.Select(s => s.IsRecord));
    }

    [Fact]
    public async Task Naming_a_template_keeps_counting_its_attempts_and_record_together()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        // Antes de agregar el NPC a data/npcs.json quedó "NPC 111"; después, con nombre. Es el mismo objetivo: la plantilla
        // manda (FightTarget.Identity), como en el historial en memoria y en el modo dungeon.
        await repository.SaveAsync(Fight(Start, null, 111, 150));
        await repository.SaveAsync(Fight(Start.AddMinutes(5), "Kromede", 111, 120));

        var listed = (await repository.ListRecentAsync()).Reverse().ToList();

        Assert.Equal([1, 2], listed.Select(s => s.Attempt));
        // El récord sigue siendo el de 150, aunque esa pelea no tenga nombre.
        Assert.Equal([true, false], listed.Select(s => s.IsRecord));
    }

    [Fact]
    public async Task Two_templates_with_the_same_name_are_two_targets()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        await repository.SaveAsync(Fight(Start, "Kromede", 111, 100));
        await repository.SaveAsync(Fight(Start.AddMinutes(5), "Kromede", 222, 120));

        var listed = (await repository.ListRecentAsync()).Reverse().ToList();

        Assert.Equal([1, 1], listed.Select(s => s.Attempt));
    }

    [Fact]
    public async Task Without_templates_the_name_decides_and_old_entity_names_are_not_names()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        await repository.SaveAsync(Fight(Start, "Kromede", null, 100));
        await repository.SaveAsync(Fight(Start.AddMinutes(5), "#81234", null, 120));
        await repository.SaveAsync(Fight(Start.AddMinutes(10), "Kromede", null, 130));
        await repository.SaveAsync(Fight(Start.AddMinutes(15), "#81234", null, 90));
        // Una pelea vieja con nombre y sin plantilla no se junta con una nueva que tiene plantilla (la plantilla manda).
        await repository.SaveAsync(Fight(Start.AddMinutes(20), "Kromede", 333, 140));

        var listed = (await repository.ListRecentAsync()).Reverse().ToList();

        Assert.Equal([1, 0, 2, 0, 1], listed.Select(s => s.Attempt));
        Assert.Equal([false, false, true, false, true], listed.Select(s => s.IsRecord));
    }

    [Fact]
    public async Task One_fight_loads_as_the_history_lists_it_even_outside_the_recent_ones()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        var first = Fight(Start, null, 111, 100);
        await repository.SaveAsync(first);
        await repository.SetNoteAsync(first.Id.ToString(), "probar otra rotación");
        await repository.SaveAsync(Fight(Start.AddMinutes(5), null, 111, 120));

        Assert.DoesNotContain(await repository.ListRecentAsync(1), s => s.Id == first.Id.ToString());
        var loaded = await repository.LoadSummaryAsync(first.Id.ToString());

        Assert.NotNull(loaded);
        Assert.Equal(("probar otra rotación", 1, (uint?)111), (loaded.Note, loaded.Attempt, loaded.TargetNpcId));
        Assert.Null(await repository.LoadSummaryAsync(Guid.NewGuid().ToString()));
    }

    private static EncounterSnapshot Fight(DateTimeOffset start, string? target, uint? npc, double localDps) => new(
        Guid.NewGuid(),
        start,
        start.AddSeconds(90),
        target,
        TargetKilled: true,
        TotalDamage: (long)(localDps * 90),
        [new ActorSnapshot(1, "Mati", null, true, (long)(localDps * 90), localDps, 1, 10, 2, 100, 0, [])],
        TargetNpcId: npc);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }
}
