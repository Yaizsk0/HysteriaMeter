using System.IO;
using HysteriaMeter.App.FightCard;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;

namespace HysteriaMeter.App.Tests;

/// <summary>Aviso de récord del overlay y tarjeta de fin de pelea: no se repiten y dicen qué métrica comparan.</summary>
public sealed class FightNoticeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Overlay_notice_is_left_out_when_the_fight_card_opens()
    {
        Assert.Equal("¡Récord en Tarka!", MeterViewModel.FinishedNotice("¡Récord en Tarka!", cardOpens: false));
        Assert.Null(MeterViewModel.FinishedNotice("¡Récord en Tarka!", cardOpens: true));
    }

    [Fact]
    public void Card_explains_that_the_comparison_uses_the_whole_fight_when_only_the_boss_counts()
    {
        var fight = Kill(T0, Player(1, "Yo", local: true, dps: 150, bossDps: 120));

        var whole = new FightCardViewModel(fight, Session(), bossOnly: false);
        Assert.Null(whole.MetricHint);

        var bossOnly = new FightCardViewModel(fight, Session(), bossOnly: true);
        Assert.Equal(Numbers.Format(120), bossOnly.Dps);
        Assert.NotNull(bossOnly.MetricHint);
        Assert.Contains(Numbers.Format(150), bossOnly.MetricHint, StringComparison.Ordinal);
        Assert.Contains("pelea entera", bossOnly.MetricHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Percentile_hint_carries_the_metric_note_with_boss_only()
    {
        var database = Path.Combine(Path.GetTempPath(), $"hysteria-aviso-{Guid.NewGuid():N}.db");
        try
        {
            var history = new EncounterRepository(database);
            await history.InitializeAsync();
            await history.SaveAsync(Kill(T0, Player(1, "Yo", local: true, dps: 100)));
            await history.SaveAsync(Kill(T0.AddMinutes(5), Player(1, "Yo", local: true, dps: 200)));

            var card = new FightCardViewModel(Kill(T0.AddMinutes(10), Player(1, "Yo", local: true, dps: 150, bossDps: 120)), Session(history), bossOnly: true);
            await card.LoadAsync();

            Assert.StartsWith("Entre tus 3 intentos contra Tarka. ", card.PercentileHint, StringComparison.Ordinal);
            Assert.EndsWith(card.MetricHint!, card.PercentileHint, StringComparison.Ordinal);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var file in new[] { database, database + "-wal", database + "-shm" })
            {
                File.Delete(file);
            }
        }
    }

    private static MeterSession Session(EncounterRepository? history = null) =>
        new(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences(), _ => { }, history: history);

    private static ActorSnapshot Player(ulong id, string name, bool local, double dps, double bossDps = 0) =>
        new(id, name, null, local, (long)(dps * 252), dps, 0, 10, 1, 100, 0, [new SkillSnapshot(100, (long)(dps * 252), 10, 1, 100, 1)], MainTargetDps: bossDps);

    private static EncounterSnapshot Kill(DateTimeOffset start, params ActorSnapshot[] actors) =>
        new(Guid.NewGuid(), start, start.AddSeconds(252), "Tarka", TargetKilled: true, actors.Sum(a => a.Damage), actors, TargetIsBoss: true);
}
