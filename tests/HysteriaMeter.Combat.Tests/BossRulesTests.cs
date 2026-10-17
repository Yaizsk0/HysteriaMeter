using HysteriaMeter.Combat;

namespace HysteriaMeter.Combat.Tests;

/// <summary>La regla de boss con datos sueltos (pelea guardada) da lo mismo que con la pelea entera.</summary>
public class BossRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private static EncounterSnapshot Fight(int seconds, string? target, bool killed, long damage, long onMain, bool boss = false, int? mainSeconds = null) =>
        new(Guid.NewGuid(), Start, Start.AddSeconds(seconds), target, killed, damage,
            [new ActorSnapshot(1, "Yo", null, true, damage, damage / (double)seconds, 1, 10, 2, 100, 0, [], MainTargetDamage: onMain)],
            MainTargetDuration: mainSeconds is { } s ? TimeSpan.FromSeconds(s) : null, TargetIsBoss: boss);

    public static TheoryData<int, string?, bool, long, long, bool, int?, RunSegmentKind> Cases => new()
    {
        { 95, "Kromede", true, 1000, 900, false, null, RunSegmentKind.Kill },
        { 95, "Kromede", false, 1000, 900, false, null, RunSegmentKind.Wipe },
        // Trash: daño repartido, pelea corta o sin objetivo con nombre.
        { 95, "Lobo", false, 1000, 300, false, null, RunSegmentKind.Trash },
        { 12, "Lobo", true, 1000, 900, false, null, RunSegmentKind.Trash },
        { 20, "Kromede", false, 1000, 900, false, null, RunSegmentKind.Trash },
        { 95, null, false, 1000, 900, false, null, RunSegmentKind.Trash },
        // El objetivo principal aguantó menos que la pelea: cuenta lo que aguantó él.
        { 95, "Lobo", true, 1000, 900, false, 10, RunSegmentKind.Trash },
        // Marcado como boss en data/npcs.json: alcanza aunque sea corto.
        { 12, "Kromede", true, 1000, 100, true, null, RunSegmentKind.Kill },
        { 12, "Kromede", false, 1000, 100, true, null, RunSegmentKind.Wipe },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Loose_values_match_the_snapshot_rule(int seconds, string? target, bool killed, long damage, long onMain, bool boss, int? mainSeconds, RunSegmentKind expected)
    {
        var fight = Fight(seconds, target, killed, damage, onMain, boss, mainSeconds);

        var attempt = BossRules.IsBossAttempt(killed, target is not null, boss, fight.Duration, fight.MainTargetDuration, damage, onMain);

        Assert.Equal(BossRules.IsBossAttempt(fight), attempt);
        Assert.Equal(BossRules.IsBossKill(fight), BossRules.IsBossKill(killed, boss, fight.Duration, fight.MainTargetDuration));
        Assert.Equal(BossRules.LooksLikeBossFight(fight), BossRules.LooksLikeBossFight(boss, damage, onMain));
        Assert.Equal(expected, BossRules.Classify(attempt, killed));
    }

    [Fact]
    public void Without_damage_it_does_not_look_like_a_boss()
    {
        Assert.False(BossRules.LooksLikeBossFight(false, 0, 0));
        Assert.True(BossRules.LooksLikeBossFight(true, 0, 0));
    }
}
