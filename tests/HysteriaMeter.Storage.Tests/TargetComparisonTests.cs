using HysteriaMeter.Combat;
using Microsoft.Data.Sqlite;

namespace HysteriaMeter.Storage.Tests;

/// <summary>
/// Las consultas del camino en vivo (récord, intento anterior, percentil, "Comparar con" e "Intento N") con la plantilla:
/// comparan con la misma regla que el historial (<see cref="FightTarget.Identity(string?, uint?)"/>, la plantilla primero
/// y, sin plantilla, el nombre), así el overlay y el historial dan el mismo número para la misma pelea.
/// </summary>
public sealed class TargetComparisonTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"hysteria-target-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task A_template_is_compared_with_its_fights_with_and_without_name()
    {
        var (repository, fights) = await SeedAsync();
        var (unnamed, named, _, _, current) = fights;
        var id = current.Id.ToString();

        // La plantilla 111 sin nombre y ya nombrada "Kromede" son el mismo objetivo; la pelea vieja "Kromede" sin plantilla
        // (300) y la plantilla 222 (500) no entran.
        Assert.Equal(150, await repository.BestLocalDpsAsync("Kromede", 111u, id, "Mati"));
        Assert.Equal(120, await repository.PreviousLocalDpsAsync("Kromede", 111u, id, "Mati"));
        Assert.Equal([120d, 150d], (await repository.LocalDpsAsync("Kromede", 111u, id, "Mati")).Order());
        Assert.Equal((named.Id.ToString(), unnamed.Id.ToString()), await repository.PastAttemptsAsync("Kromede", 111u, id, "Mati"));

        // Sin nombre todavía (como en el overlay antes de nombrarla en data/npcs.json) da lo mismo: manda la plantilla.
        Assert.Equal(150, await repository.BestLocalDpsAsync(null, 111u, id, "Mati"));

        // Las consultas por nombre quedan como estaban: "Kromede" junta la pelea vieja sin plantilla y la nombrada.
        Assert.Equal(300, await repository.BestLocalDpsAsync("Kromede", id, "Mati"));
    }

    [Fact]
    public async Task The_live_attempt_matches_the_history_attempt()
    {
        var (repository, fights) = await SeedAsync();
        var current = fights.Current;

        var earlier = await repository.CountEarlierAttemptsAsync(current.TargetName, current.TargetNpcId, current.Start, current.Id.ToString(), "Mati");
        var listed = (await repository.ListRecentAsync()).Single(s => s.Id == current.Id.ToString());

        // El overlay dice "Intento N" con N = anteriores + 1: el mismo 3 que el historial para esa pelea.
        Assert.Equal(2, earlier);
        Assert.Equal(listed.Attempt, earlier + 1);

        // Mientras la pelea sigue en curso (sin guardar), cuenta las tres guardadas de esa plantilla con ese personaje.
        Assert.Equal(3, await repository.CountEarlierAttemptsAsync(null, 111u, Start.AddHours(1), Guid.NewGuid().ToString(), "Mati"));
        Assert.Equal(0, await repository.CountEarlierAttemptsAsync(null, 111u, Start.AddHours(1), Guid.NewGuid().ToString(), "Alt"));
    }

    [Fact]
    public async Task Without_a_template_the_name_decides_among_the_fights_without_one()
    {
        var (repository, fights) = await SeedAsync();
        var id = fights.Current.Id.ToString();

        // "Kromede" sin plantilla es la serie de las peleas viejas sin plantilla: la nombrada con plantilla no entra.
        Assert.Equal(300, await repository.BestLocalDpsAsync("Kromede", npcId: null, id, "Mati"));
        Assert.Equal(
            (fights.OldNamed.Id.ToString(), fights.OldNamed.Id.ToString()),
            await repository.PastAttemptsAsync("Kromede", npcId: null, id, "Mati"));
        Assert.Equal(1, await repository.CountEarlierAttemptsAsync("Kromede", npcId: null, Start.AddHours(1), id, "Mati"));
    }

    [Fact]
    public async Task Without_name_or_template_nothing_is_compared()
    {
        var (repository, fights) = await SeedAsync();
        var id = fights.Current.Id.ToString();

        // Sin nombre ni plantilla (o con el "#entidad" de versiones viejas) no hay con qué compararla, como en el historial.
        foreach (var name in new string?[] { null, string.Empty, "#81234" })
        {
            Assert.Null(await repository.BestLocalDpsAsync(name, npcId: null, id, "Mati"));
            Assert.Null(await repository.PreviousLocalDpsAsync(name, npcId: null, id, "Mati"));
            Assert.Empty(await repository.LocalDpsAsync(name, npcId: null, id, "Mati"));
            Assert.Equal(((string?)null, (string?)null), await repository.PastAttemptsAsync(name, npcId: null, id, "Mati"));
            Assert.Equal(0, await repository.CountEarlierAttemptsAsync(name, npcId: null, Start.AddHours(1), id, "Mati"));
        }
    }

    [Fact]
    public async Task A_short_pull_is_trash_and_is_left_out_of_every_comparison()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        var kill = Fight(Start, null, 333, 200);
        // 10 s a 900 DPS sin kill: el arranque de un pull que se reseteó. Es trash (menos de 30 s).
        var pull = Fight(Start.AddMinutes(5), null, 333, 900, seconds: 10, killed: false);
        // Un wipe de 60 s con casi todo el daño al boss sí es un intento.
        var wipe = Fight(Start.AddMinutes(8), null, 333, 180, seconds: 60, killed: false, mainShare: 0.9);
        // Un wipe de 60 s con el daño repartido (adds) es trash.
        var adds = Fight(Start.AddMinutes(11), null, 333, 700, seconds: 60, killed: false, mainShare: 0.2);
        var current = Fight(Start.AddMinutes(15), null, 333, 210);
        foreach (var fight in new[] { kill, pull, wipe, adds, current })
        {
            await repository.SaveAsync(fight);
        }

        var id = current.Id.ToString();
        Assert.Equal(200, await repository.BestLocalDpsAsync(null, 333u, id, "Mati"));
        Assert.Equal(180, await repository.PreviousLocalDpsAsync(null, 333u, id, "Mati"));
        Assert.Equal([180d, 200d], (await repository.LocalDpsAsync(null, 333u, id, "Mati")).Order());
        Assert.Equal((wipe.Id.ToString(), kill.Id.ToString()), await repository.PastAttemptsAsync(null, 333u, id, "Mati"));

        // La ★ del historial: la pelea actual (210) es el récord entre los intentos de boss, aunque el pull hizo 900.
        var listed = await repository.ListRecentAsync();
        Assert.True(listed.Single(s => s.Id == id).IsRecord);
        Assert.False(listed.Single(s => s.Id == kill.Id.ToString()).IsRecord);

        // El "intento N" sigue contando todas las peleas contra la plantilla, trash incluido (decisiones.md).
        Assert.Equal(5, listed.Single(s => s.Id == id).Attempt);
        Assert.Equal(4, await repository.CountEarlierAttemptsAsync(null, 333u, current.Start, id, "Mati"));
    }

    /// <summary>La lista de NPCs de los tests de abajo: 444 es un boss marcado y 555 un NPC con nombre, sin marca.</summary>
    private static readonly NpcCatalog Marked = new(new Dictionary<uint, NpcInfo>
    {
        [444] = new("Ferocious Horn Nuakum", IsBoss: true),
        [555] = new("Hellfire Spirit"),
    });

    /// <summary>
    /// Las comparaciones del historial (récord, intento anterior, percentil, "Comparar con" y la ★) toman las mismas peleas
    /// que el vivo llama intento de boss: <see cref="BossRules.IsBossAttempt(EncounterSnapshot)"/> con la marca de la lista.
    /// Un boss marcado cuenta desde los 10 s aunque el daño se reparta; un toque más corto es trash, como sin marca.
    /// </summary>
    [Theory]
    [InlineData(true, true, 3, null, 1.0)]
    [InlineData(true, true, 10, null, 1.0)]
    [InlineData(true, true, 15, null, 1.0)]
    [InlineData(true, true, 95, 8, 1.0)]
    [InlineData(true, false, 5, null, 1.0)]
    [InlineData(true, false, 12, null, 0.2)]
    [InlineData(false, true, 15, null, 1.0)]
    [InlineData(false, false, 12, null, 1.0)]
    [InlineData(false, true, 35, null, 1.0)]
    [InlineData(false, false, 35, null, 0.2)]
    [InlineData(false, false, 35, null, 0.9)]
    public async Task A_saved_fight_is_compared_when_the_live_rule_calls_it_a_boss_attempt(
        bool marked, bool killed, int seconds, int? mainSeconds, double mainShare)
    {
        var repository = new EncounterRepository(_path) { Npcs = () => Marked };
        await repository.InitializeAsync();
        var npc = marked ? 444u : 555u;
        var fight = Fight(Start, null, npc, 700, seconds, killed, mainShare) with
        {
            MainTargetDuration = mainSeconds is { } main ? TimeSpan.FromSeconds(main) : null,
        };
        var current = Fight(Start.AddMinutes(5), null, npc, 300);
        await repository.SaveAsync(fight);
        await repository.SaveAsync(current);

        // Lo que el vivo dice de esa pelea cuando terminó: el motor le puso la marca de la lista.
        var live = BossRules.IsBossAttempt(fight with { TargetIsBoss = Marked.IsBoss(npc) });

        var id = current.Id.ToString();
        double? expected = live ? 700 : null;
        Assert.Equal(live, (await repository.LocalDpsAsync(null, npc, id, "Mati")).Contains(700));
        Assert.Equal(expected, await repository.BestLocalDpsAsync(null, npc, id, "Mati"));
        Assert.Equal(expected, await repository.PreviousLocalDpsAsync(null, npc, id, "Mati"));
        Assert.Equal(live ? fight.Id.ToString() : null, (await repository.PastAttemptsAsync(null, npc, id, "Mati")).Best);

        // La ★ del historial: si la de 700 fue un intento de boss, la actual (300) no es el récord.
        var listed = await repository.ListRecentAsync();
        Assert.Equal(!live, listed.Single(s => s.Id == id).IsRecord);
        Assert.Equal(!live, (await repository.LoadSummaryAsync(id))!.IsRecord);
    }

    [Fact]
    public async Task The_list_is_read_on_every_query_so_a_new_list_counts_at_once()
    {
        var npcs = NpcCatalog.Empty;
        var repository = new EncounterRepository(_path) { Npcs = () => npcs };
        await repository.InitializeAsync();
        var kill = Fight(Start, null, 444, 700, seconds: 15);
        var current = Fight(Start.AddMinutes(5), null, 444, 300);
        await repository.SaveAsync(kill);
        await repository.SaveAsync(current);
        var id = current.Id.ToString();

        // Sin la marca, un kill de 15 s es trash (la regla de 30 s); con la lista que lo marca, es un intento.
        Assert.Null(await repository.BestLocalDpsAsync(null, 444u, id, "Mati"));
        npcs = Marked;
        Assert.Equal(700, await repository.BestLocalDpsAsync(null, 444u, id, "Mati"));

        // Un repositorio sin lista (las herramientas, los tests viejos) se queda con la regla de 30 s.
        var withoutList = new EncounterRepository(_path);
        Assert.Null(await withoutList.BestLocalDpsAsync(null, 444u, id, "Mati"));
    }

    private async Task<(EncounterRepository Repository, (EncounterSnapshot Unnamed, EncounterSnapshot Named, EncounterSnapshot OldNamed, EncounterSnapshot Other, EncounterSnapshot Current) Fights)> SeedAsync()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        var unnamed = Fight(Start, null, 111, 150);
        var named = Fight(Start.AddMinutes(5), "Kromede", 111, 120);
        var oldNamed = Fight(Start.AddMinutes(10), "Kromede", null, 300);
        var other = Fight(Start.AddMinutes(12), null, 222, 500);
        var anonymous = Fight(Start.AddMinutes(14), "#81234", null, 900);
        var current = Fight(Start.AddMinutes(15), "Kromede", 111, 130);
        foreach (var fight in new[] { unnamed, named, oldNamed, other, anonymous, current })
        {
            await repository.SaveAsync(fight);
        }

        return (repository, (unnamed, named, oldNamed, other, current));
    }

    private static EncounterSnapshot Fight(
        DateTimeOffset start, string? target, uint? npc, double localDps, int seconds = 90, bool killed = true, double mainShare = 1)
    {
        var damage = (long)(localDps * seconds);
        return new(
            Guid.NewGuid(),
            start,
            start.AddSeconds(seconds),
            target,
            TargetKilled: killed,
            TotalDamage: damage,
            [new ActorSnapshot(1, "Mati", null, true, damage, localDps, 1, 10, 2, 100, 0, [], MainTargetDamage: (long)(damage * mainShare))],
            TargetNpcId: npc);
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
