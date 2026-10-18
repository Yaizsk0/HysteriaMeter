using HysteriaMeter.Combat;

namespace HysteriaMeter.Combat.Tests;

/// <summary>
/// Bosses sin nombre en data/npcs.json: se agrupan por plantilla de NPC, nunca por "sin nombre". Los wipes son intentos
/// reales sin kill (no "intentos − 1") y una pelea vieja sin plantilla ni nombre no se junta con ninguna.
/// </summary>
public class DungeonSummaryKeysTests
{
    private static readonly DateTimeOffset Entered = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private static ActorSnapshot Player(string name, long damage, double seconds, int deaths = 0, bool local = false) =>
        new(1, name, 15, local, damage, damage / seconds, 0, 10, 2, 100, 0, [],
            MainTargetDamage: damage, MainTargetDps: damage / seconds, Deaths: deaths);

    private static EncounterSnapshot Fight(
        int startSecond, int seconds, uint? npc, bool killed, string? target = null, IReadOnlyList<HealthPhase>? phases = null, params ActorSnapshot[] actors)
    {
        actors = actors.Length > 0 ? actors : [Player("Yo", 1000L * seconds, seconds, local: true)];
        return new(Guid.NewGuid(), Entered.AddSeconds(startSecond), Entered.AddSeconds(startSecond + seconds), target, killed,
            actors.Sum(a => a.Damage), actors, TargetNpcId: npc, ZoneEnteredAt: Entered, Phases: phases);
    }

    [Fact]
    public void Three_kills_of_three_unnamed_bosses_are_three_cards_without_wipes()
    {
        var summary = DungeonSummary.From(
            [Fight(0, 90, 2400101, true), Fight(200, 100, 2400102, true), Fight(400, 110, 2400103, true)], Entered);

        Assert.Equal(["NPC 2400101", "NPC 2400102", "NPC 2400103"], summary.Bosses.Select(b => b.Name));
        Assert.All(summary.Bosses, b => Assert.Equal((1, true, 0), (b.Attempts, b.Killed, b.Wipes)));
        Assert.Equal((3, 0), (summary.BossesKilled, summary.Wipes));
        Assert.Equal(["NPC 2400101", "NPC 2400102", "NPC 2400103"], summary.Timeline.Select(s => s.Label));
    }

    [Fact]
    public void Old_kills_without_npc_id_or_name_are_not_merged_into_one_boss()
    {
        var fights = new[] { Fight(0, 90, null, true), Fight(200, 100, null, true), Fight(400, 110, null, true) };

        var summary = DungeonSummary.From(fights, Entered);

        Assert.Equal(["Boss 1", "Boss 2", "Boss 3"], summary.Bosses.Select(b => b.Name));
        Assert.Equal(0, summary.Wipes);
        Assert.Equal(fights.Select(f => f.Id), summary.Bosses.Select(b => b.DecidingId));
    }

    [Fact]
    public void An_old_fight_without_npc_id_or_name_that_did_not_die_is_not_a_wipe()
    {
        // Sin plantilla ni nombre no hay cómo saber que fue un boss: no es un wipe que no se puede probar. Tampoco se lo
        // llama trash: el tramo gris dice "Encuentro".
        var summary = DungeonSummary.From([Fight(0, 120, null, false), Fight(200, 100, null, true)], Entered);

        Assert.Equal(0, summary.Wipes);
        Assert.Equal([RunSegmentKind.Trash, RunSegmentKind.Kill], summary.Timeline.Select(s => s.Kind));
        Assert.Equal(["Encuentro", "Boss 1"], summary.Timeline.Select(s => s.Label));
    }

    [Fact]
    public void A_real_wipe_against_an_unnamed_boss_counts_once_and_the_kill_decides_the_dps()
    {
        var wipe = Fight(0, 120, 2400200, false, phases: [new HealthPhase(75, 40), new HealthPhase(50, 90)],
            actors: [Player("Yo", 60_000, 120, deaths: 1, local: true), Player("Rubia", 30_000, 120, deaths: 1)]);
        var kill = Fight(300, 150, 2400200, true,
            actors: [Player("Yo", 45_000, 150, local: true), Player("Rubia", 75_000, 150)]);

        var boss = Assert.Single(DungeonSummary.From([wipe, kill], Entered).Bosses);

        Assert.Equal(("NPC 2400200", 2, true, 1), (boss.Name, boss.Attempts, boss.Killed, boss.Wipes));
        Assert.Equal(kill.Id, boss.DecidingId);
        Assert.Equal([(wipe.Id, TimeSpan.FromSeconds(120), (int?)50)], boss.WipeList.Select(w => (w.FightId, w.Duration, w.BelowPercent)));
        Assert.Equal([("Rubia", 500.0, 1), ("Yo", 300.0, 1)], boss.Players.Select(p => (p.Name, p.Dps, p.Deaths)));
        Assert.Equal(800, boss.PartyDps);
    }

    [Fact]
    public void A_boss_without_a_kill_has_as_many_wipes_as_attempts()
    {
        var summary = DungeonSummary.From([Fight(0, 120, 2400300, false), Fight(300, 100, 2400300, false)], Entered);

        var boss = Assert.Single(summary.Bosses);
        Assert.Equal((2, false, 2), (boss.Attempts, boss.Killed, boss.Wipes));
        Assert.Null(boss.WipeList[0].BelowPercent);
    }

    [Fact]
    public void The_same_template_killed_twice_is_two_bosses_not_a_wipe()
    {
        var summary = DungeonSummary.From([Fight(0, 90, 2400400, true), Fight(300, 95, 2400400, true)], Entered);

        Assert.Equal(["NPC 2400400", "NPC 2400400 (2)"], summary.Bosses.Select(b => b.Name));
        Assert.Equal(0, summary.Wipes);
    }

    [Fact]
    public void A_named_boss_keeps_its_name_and_the_timeline_points_to_each_fight()
    {
        var trash = Fight(0, 12, 7, true, "Lobo");
        var kill = Fight(100, 120, 2300371, true, "Kromede");

        var summary = DungeonSummary.From([trash, kill], Entered);

        Assert.Equal(["Kromede"], summary.Bosses.Select(b => b.Name));
        Assert.Equal([("Trash", trash.Id), ("Kromede", kill.Id)], summary.Timeline.Select(s => (s.Label, s.FightId)));
    }

    [Theory]
    [InlineData("Kromede", 2300371u, "Kromede")]
    [InlineData(null, 2400123u, "NPC 2400123")]
    [InlineData("#81234", 2400123u, "NPC 2400123")]
    [InlineData(null, null, null)]
    [InlineData("#81234", null, null)]
    [InlineData("", null, null)]
    public void The_target_label_is_the_name_or_else_the_npc_template(string? name, uint? npc, string? expected)
    {
        Assert.Equal(expected, FightTarget.NameOrNpc(name, npc));
        Assert.Equal(expected ?? "Encuentro", FightTarget.Label(name, npc));
    }

    [Fact]
    public void A_non_kill_with_an_npc_id_and_no_name_is_a_boss_attempt()
    {
        var wipe = Fight(0, 120, 2400500, false);

        Assert.True(BossRules.IsBossAttempt(wipe));
        Assert.False(BossRules.IsBossAttempt(wipe with { TargetNpcId = null }));
    }
}
