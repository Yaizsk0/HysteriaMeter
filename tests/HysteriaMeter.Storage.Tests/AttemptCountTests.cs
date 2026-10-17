using HysteriaMeter.Combat;

namespace HysteriaMeter.Storage.Tests;

/// <summary>"Intento N" del overlay en vivo: cuenta las peleas guardadas antes de la actual contra el mismo objetivo.</summary>
public sealed class AttemptCountTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"hysteria-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Counts_earlier_fights_against_the_same_target_and_character()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        var start = new DateTimeOffset(2026, 10, 15, 21, 0, 0, TimeSpan.Zero);
        var first = Fight(start, "Tarka");
        var second = Fight(start.AddMinutes(10), "Tarka");
        var other = Fight(start.AddMinutes(15), "Otro");
        var alt = Fight(start.AddMinutes(18), "Tarka", localName: "Alt");
        foreach (var fight in new[] { first, second, other, alt })
        {
            await repository.SaveAsync(fight);
        }

        var live = Guid.NewGuid().ToString();
        var now = start.AddMinutes(20);

        // La pelea en curso todavía no está guardada: dos anteriores con "Mati" → es el intento 3.
        Assert.Equal(2, await repository.CountEarlierAttemptsAsync("Tarka", now, live, "Mati"));
        Assert.Equal(1, await repository.CountEarlierAttemptsAsync("Tarka", now, live, "Alt"));
        Assert.Equal(1, await repository.CountEarlierAttemptsAsync("Otro", now, live, "Mati"));
        Assert.Equal(0, await repository.CountEarlierAttemptsAsync("Nuevo", now, live, "Mati"));

        // Ya guardada, no se cuenta a sí misma ni a las que vinieron después.
        Assert.Equal(1, await repository.CountEarlierAttemptsAsync("Tarka", second.Start, second.Id.ToString(), "Mati"));
        Assert.Equal(0, await repository.CountEarlierAttemptsAsync("Tarka", first.Start, first.Id.ToString(), "Mati"));
    }

    [Fact]
    public async Task Without_a_local_player_counts_the_fights_without_one()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        var start = new DateTimeOffset(2026, 10, 15, 21, 0, 0, TimeSpan.Zero);
        var anonymous = Fight(start, "Tarka") with { Actors = [new ActorSnapshot(2, "Otro", null, false, 600, 10, 1, 10, 2, 100, 0, [])] };
        await repository.SaveAsync(anonymous);
        await repository.SaveAsync(Fight(start.AddMinutes(5), "Tarka"));

        // Igual que el intento del historial ("IS" también compara null): sin personaje propio, solo las peleas sin personaje.
        Assert.Equal(1, await repository.CountEarlierAttemptsAsync("Tarka", start.AddMinutes(10), Guid.NewGuid().ToString(), null));
    }

    private static EncounterSnapshot Fight(DateTimeOffset start, string target, string localName = "Mati") => new(
        Guid.NewGuid(),
        start,
        start.AddSeconds(60),
        target,
        TargetKilled: false,
        TotalDamage: 6000,
        [new ActorSnapshot(1, localName, null, true, 6000, 100, 1, 10, 2, 100, 0, [])]);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }
}
