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
