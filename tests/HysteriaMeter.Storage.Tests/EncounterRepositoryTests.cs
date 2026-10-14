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

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }
}
