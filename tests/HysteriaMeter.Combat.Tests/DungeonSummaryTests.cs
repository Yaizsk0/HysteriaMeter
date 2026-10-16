using HysteriaMeter.Combat;

namespace HysteriaMeter.Combat.Tests;

public class DungeonSummaryTests
{
    private static readonly DateTimeOffset Entered = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private static ActorSnapshot Player(string name, long damage, double seconds, int deaths = 0, bool local = false, long? mainTarget = null) =>
        new(1, name, 15, local, damage, damage / seconds, 0, 10, 2, 100, 0, [],
            MainTargetDamage: mainTarget ?? damage, MainTargetDps: (mainTarget ?? damage) / seconds, Deaths: deaths);

    private static EncounterSnapshot Fight(int startSecond, int seconds, string target, bool killed, bool boss, uint? npc, params ActorSnapshot[] actors) =>
        new(Guid.NewGuid(), Entered.AddSeconds(startSecond), Entered.AddSeconds(startSecond + seconds), target, killed,
            actors.Sum(a => a.Damage), actors, TargetNpcId: npc, TargetIsBoss: boss, ZoneEnteredAt: Entered);

    [Fact]
    public void Groups_attempts_by_boss_and_sums_deaths_over_all_of_them()
    {
        var fights = new[]
        {
            Fight(10, 20, "Lobo", true, false, 100, Player("Yo", 1000, 20, local: true, mainTarget: 300), Player("Rubia", 900, 20, mainTarget: 200)),
            Fight(60, 120, "Kromede", false, true, 2300371, Player("Yo", 30000, 120, deaths: 1, local: true), Player("Rubia", 28000, 120, deaths: 1)),
            Fight(300, 240, "Kromede", true, true, 2300371, Player("Yo", 72000, 240, deaths: 2, local: true), Player("Rubia", 96000, 240)),
        };

        var summary = DungeonSummary.From(fights, Entered);

        var boss = Assert.Single(summary.Bosses);
        Assert.Equal(("Kromede", 2, true, 1), (boss.Name, boss.Attempts, boss.Killed, boss.Wipes));
        Assert.Equal(4, boss.Deaths);
        Assert.Equal(["Rubia", "Yo"], boss.Players.Select(p => p.Name));
        Assert.Equal((400, 1), (boss.Players[0].Dps, boss.Players[0].Deaths));
        Assert.Equal((300, 3), (boss.Players[1].Dps, boss.Players[1].Deaths));
        Assert.Equal(700, boss.PartyDps);
        Assert.Equal(TimeSpan.FromSeconds(540), summary.Duration);
        Assert.Equal((1, 1, 4), (summary.BossesKilled, summary.Wipes, summary.Deaths));
        Assert.Equal([RunSegmentKind.Trash, RunSegmentKind.Wipe, RunSegmentKind.Kill], summary.Timeline.Select(s => s.Kind));
        Assert.Equal(10, summary.Timeline[0].StartSecond);
    }

    [Fact]
    public void A_long_fight_against_one_unmarked_target_counts_as_a_boss_but_a_quick_pull_does_not()
    {
        var fights = new[]
        {
            Fight(0, 12, "Guardia", true, false, 7, Player("Yo", 5000, 12)),
            Fight(40, 95, "Guardián del Umbral", true, false, 8, Player("Yo", 95000, 95)),
            Fight(200, 50, "Pack de lobos", false, false, 9, Player("Yo", 9000, 50, mainTarget: 2000)),
        };

        var summary = DungeonSummary.From(fights);

        Assert.Equal(["Guardián del Umbral"], summary.Bosses.Select(b => b.Name));
        Assert.Equal([RunSegmentKind.Trash, RunSegmentKind.Kill, RunSegmentKind.Trash], summary.Timeline.Select(s => s.Kind));
    }

    [Fact]
    public void A_player_who_died_only_in_a_wipe_still_shows_up_on_that_boss()
    {
        var fights = new[]
        {
            Fight(0, 60, "Kromede", false, true, 1, Player("Yo", 6000, 60), Player("Pepe", 3000, 60, deaths: 1)),
            Fight(100, 60, "Kromede", true, true, 1, Player("Yo", 9000, 60)),
        };

        var boss = Assert.Single(DungeonSummary.From(fights).Bosses);

        Assert.Equal([("Yo", 0), ("Pepe", 1)], boss.Players.Select(p => (p.Name, p.Deaths)));
    }

    [Fact]
    public void An_empty_run_has_no_bosses()
    {
        var summary = DungeonSummary.From([], Entered);

        Assert.Empty(summary.Bosses);
        Assert.Equal(TimeSpan.Zero, summary.Duration);
    }
}
