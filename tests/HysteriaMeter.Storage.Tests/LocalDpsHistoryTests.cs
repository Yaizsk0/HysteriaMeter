using HysteriaMeter.Combat;

namespace HysteriaMeter.Storage.Tests;

/// <summary>Los DPS propios guardados contra un objetivo, para el percentil del detalle.</summary>
public sealed class LocalDpsHistoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"hysteria-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Lists_my_dps_against_that_target_without_the_current_fight()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        var start = new DateTimeOffset(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);
        var first = Fight(start, "Tarka", localDps: 1000);
        var second = Fight(start.AddMinutes(10), "Tarka", localDps: 1500);
        var current = Fight(start.AddMinutes(20), "Tarka", localDps: 1200);
        var other = Fight(start.AddMinutes(30), "Otro", localDps: 50);
        var alt = Fight(start.AddMinutes(40), "Tarka", localDps: 9000, localName: "Alt");
        var noLocal = Fight(start.AddMinutes(50), "Tarka", localDps: 7000, isLocal: false);
        foreach (var fight in new[] { first, second, current, other, alt, noLocal })
        {
            await repository.SaveAsync(fight);
        }

        var mine = await repository.LocalDpsAsync("Tarka", current.Id.ToString(), "Mati");

        Assert.Equal([1000d, 1500d], mine.Order());
        Assert.Equal(3, (await repository.LocalDpsAsync("Tarka", current.Id.ToString())).Count);
        Assert.Empty(await repository.LocalDpsAsync("Nadie", current.Id.ToString()));
    }

    private static EncounterSnapshot Fight(DateTimeOffset start, string target, double localDps, string localName = "Mati", bool isLocal = true) => new(
        Guid.NewGuid(),
        start,
        start.AddSeconds(60),
        target,
        TargetKilled: false,
        TotalDamage: (long)(localDps * 60),
        [new ActorSnapshot(1, localName, null, isLocal, (long)(localDps * 60), localDps, 1, 10, 2, 100, 0, [])]);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }
}
