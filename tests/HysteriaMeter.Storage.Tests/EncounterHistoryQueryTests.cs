using HysteriaMeter.Combat;

namespace HysteriaMeter.Storage.Tests;

/// <summary>Lo que trae ListRecentAsync para la ventana del historial: muertes de la pelea y los datos de BossRules.</summary>
public sealed class EncounterHistoryQueryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"hysteria-{Guid.NewGuid():N}.db");

    private static readonly DateTimeOffset Start = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private static ActorSnapshot Player(ulong id, string name, bool local, long damage, long onMain, int deaths) =>
        new(id, name, null, local, damage, damage / 60.0, 0, 10, 2, 100, 0, [], MainTargetDamage: onMain, Deaths: deaths);

    [Fact]
    public async Task Sums_deaths_and_damage_of_the_whole_party()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        var fight = new EncounterSnapshot(
            Guid.NewGuid(), Start, Start.AddSeconds(60), "Kromede", TargetKilled: false, TotalDamage: 5000,
            [Player(1, "Mati", true, 3000, 2700, 1), Player(2, "Rubia", false, 2000, 1500, 2)],
            MainTargetDuration: TimeSpan.FromSeconds(55.5));
        await repository.SaveAsync(fight);

        var saved = Assert.Single(await repository.ListRecentAsync());

        Assert.Equal(3, saved.Deaths);
        Assert.Equal(5000, saved.ActorsDamage);
        Assert.Equal(4200, saved.MainTargetDamage);
        Assert.Equal(55.5, saved.MainTargetSeconds);
    }

    [Fact]
    public async Task Fights_without_players_or_main_target_come_back_empty()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();
        await repository.SaveAsync(new EncounterSnapshot(Guid.NewGuid(), Start, Start.AddSeconds(8), null, false, 0, []));
        await repository.SaveAsync(new EncounterSnapshot(
            Guid.NewGuid(), Start.AddMinutes(1), Start.AddMinutes(2), "Lobo", true, 900, [Player(1, "Mati", true, 900, 0, 0)]));

        var recent = await repository.ListRecentAsync();

        // La más nueva primero: la pelea con jugador y sin muertes da 0; la que no tiene jugadores, null.
        Assert.Equal((0, 900L, 0L), (recent[0].Deaths, recent[0].ActorsDamage, recent[0].MainTargetDamage));
        Assert.Null(recent[0].MainTargetSeconds);
        Assert.Null(recent[1].Deaths);
        Assert.Equal(0, recent[1].ActorsDamage);
    }

    [Fact]
    public async Task Saved_fights_classify_like_the_dungeon_mode()
    {
        var repository = new EncounterRepository(_path);
        await repository.InitializeAsync();

        // Wipe: el objetivo se llevó casi todo el daño durante un minuto. Trash: el daño repartido entre varios bichos.
        var wipe = new EncounterSnapshot(Guid.NewGuid(), Start, Start.AddSeconds(60), "Kromede", false, 3000,
            [Player(1, "Mati", true, 3000, 2900, 1)], MainTargetDuration: TimeSpan.FromSeconds(58));
        var trash = new EncounterSnapshot(Guid.NewGuid(), Start.AddMinutes(2), Start.AddMinutes(3), "Lobo", false, 3000,
            [Player(1, "Mati", true, 3000, 600, 0)], MainTargetDuration: TimeSpan.FromSeconds(12));
        await repository.SaveAsync(wipe);
        await repository.SaveAsync(trash);

        var attempts = (await repository.ListRecentAsync()).Select(saved =>
        {
            var duration = DateTimeOffset.Parse(saved.EndedAt, System.Globalization.CultureInfo.InvariantCulture)
                - DateTimeOffset.Parse(saved.StartedAt, System.Globalization.CultureInfo.InvariantCulture);
            return BossRules.IsBossAttempt(
                saved.TargetKilled, saved.TargetName is not null, targetIsBoss: false, duration,
                saved.MainTargetSeconds is { } s ? TimeSpan.FromSeconds(s) : null, saved.ActorsDamage, saved.MainTargetDamage);
        }).ToList();

        // La más nueva primero (el trash), y lo mismo que da la regla con la pelea entera.
        Assert.Equal([false, true], attempts);
        Assert.Equal([BossRules.IsBossAttempt(trash), BossRules.IsBossAttempt(wipe)], attempts);
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
