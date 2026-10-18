using HysteriaMeter.Combat;

namespace HysteriaMeter.Combat.Tests;

/// <summary>
/// Una sola regla para "el mismo objetivo" (FightTarget.Identity): la plantilla primero y, sin plantilla, el nombre. Es
/// la que usan el SQL del historial, el historial en memoria y el modo dungeon. Ponerle nombre a una plantilla en
/// data/npcs.json no corta los intentos, y el modo dungeon cuenta los bosses con BossRules.GroupAttempts, igual que el
/// "2/3 bosses · 1 wipe" del historial.
/// </summary>
public class TargetIdentityTests
{
    private static readonly DateTimeOffset Entered = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private static EncounterSnapshot Fight(int startSecond, int seconds, uint? npc, bool killed, string? target = null) =>
        new(Guid.NewGuid(), Entered.AddSeconds(startSecond), Entered.AddSeconds(startSecond + seconds), target, killed, 1000L * seconds,
            [new ActorSnapshot(1, "Yo", 15, true, 1000L * seconds, 1000, 0, 10, 2, 100, 0, [], MainTargetDamage: 1000L * seconds, MainTargetDps: 1000)],
            TargetNpcId: npc, ZoneEnteredAt: Entered);

    [Theory]
    [InlineData("Kromede", 2300371u, "npc:2300371")]
    [InlineData(null, 2300371u, "npc:2300371")]
    [InlineData("#81234", 2300371u, "npc:2300371")]
    [InlineData("Kromede", null, "nombre:Kromede")]
    [InlineData("#81234", null, null)]
    [InlineData("", null, null)]
    [InlineData(null, null, null)]
    public void The_identity_is_the_template_first_and_then_the_name(string? name, uint? npc, string? expected) =>
        Assert.Equal(expected, FightTarget.Identity(name, npc));

    [Fact]
    public void Naming_a_template_keeps_the_old_unnamed_attempts_on_the_same_boss()
    {
        // El wipe quedó "NPC 2400123"; después se agregó el ID a data/npcs.json y el kill ya tiene nombre.
        var wipe = Fight(0, 120, 2400123, false);
        var kill = Fight(300, 150, 2400123, true, "Griffith");

        var boss = Assert.Single(DungeonSummary.From([wipe, kill], Entered).Bosses);

        Assert.Equal(("Griffith", 2, 1), (boss.Name, boss.Attempts, boss.Wipes));
        Assert.Equal(FightTarget.Identity(wipe), FightTarget.Identity(kill));
    }

    [Fact]
    public void Two_templates_with_the_same_name_are_two_bosses()
    {
        var summary = DungeonSummary.From([Fight(0, 90, 2400001, true, "Griffith"), Fight(300, 90, 2400002, true, "Griffith")], Entered);

        Assert.Equal(["Griffith", "Griffith (2)"], summary.Bosses.Select(b => b.Name));
    }

    [Fact]
    public void A_kill_closes_the_boss_and_a_wipe_after_it_is_another_boss()
    {
        (string Id, string? Key, bool Killed)[] attempts =
            [("a", "npc:1", false), ("b", "npc:1", true), ("c", "npc:1", false), ("d", "npc:1", true), ("e", "npc:1", true)];

        var groups = BossRules.GroupAttempts(attempts, f => f.Key, f => f.Killed);

        Assert.Equal(["a b", "c d", "e"], groups.Select(g => string.Join(' ', g.Select(f => f.Id))));
    }

    [Fact]
    public void Attempts_without_identity_are_never_merged()
    {
        (string Id, string? Key)[] attempts = [("a", null), ("b", null), ("c", "npc:7")];

        Assert.Equal(3, BossRules.GroupAttempts(attempts, f => f.Key, _ => true).Count);
    }
}
