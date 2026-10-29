using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat.Tests;

// "Invocaciones separadas" (T021): el daño de las invocaciones sigue sumado al dueño, pero sus skills quedan marcadas.
// Eventos sintéticos: no contienen nombres ni IDs de entidades de capturas reales.
public class SummonSeparationTests
{
    private const ulong Me = 1;
    private const ulong Caster = 2;
    private const ulong Summon = 50;
    private const ulong Boss = 900;
    private const uint OwnSkill = 15_030_340;
    private const uint SummonSkill = 15_280_143;
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData("id")]
    [InlineData("nombre")]
    [InlineData("clase")]
    public void Summon_hits_add_to_the_owner_and_are_marked_as_from_a_summon(string ownership)
    {
        var engine = CreateEngine();
        switch (ownership)
        {
            case "id":
                engine.Apply(new EntitySpawnEvent(T0, Summon, Caster, NpcId: 2_920_650));
                break;
            case "nombre":
                engine.Apply(new EntitySpawnEvent(T0, Summon, null, NpcId: 2_920_650, OwnerName: "Hechicera"));
                break;
            default:
                engine.Apply(new EntitySpawnEvent(T0, Summon, null, NpcId: 2_920_650)); // se adopta por la clase
                break;
        }

        engine.Apply(new DamageEvent(At(1), Caster, Boss, OwnSkill, 1_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Summon, Boss, SummonSkill, 2_000, HitModifiers.Critical));
        engine.Apply(new DamageEvent(At(3), Summon, Boss, SummonSkill, 500, HitModifiers.DamageOverTime));

        var actor = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal((Caster, 3_500L, 2_500L), (actor.Id, actor.Damage, actor.SummonDamage));
        Assert.Equal(
            [(SummonSkill, true, 2_500L), (OwnSkill, false, 1_000L)],
            actor.Skills.Select(s => (s.SkillId, s.FromSummon, s.Damage)));
    }

    [Fact]
    public void The_same_skill_from_the_player_and_its_summon_is_two_entries()
    {
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, Summon, Caster, NpcId: 2_920_650));

        engine.Apply(new DamageEvent(At(1), Caster, Boss, SummonSkill, 1_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Summon, Boss, SummonSkill, 3_000, HitModifiers.None));

        var actor = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal(
            [(SummonSkill, true, 3_000L, 1), (SummonSkill, false, 1_000L, 1)],
            actor.Skills.Select(s => (s.SkillId, s.FromSummon, s.Damage, s.Hits)));
        Assert.Equal((4_000L, 3_000L, 2), (actor.Damage, actor.SummonDamage, actor.Hits));
    }

    [Fact]
    public void Session_and_run_mark_the_same_summon_damage_as_the_fight()
    {
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, Summon, Caster, NpcId: 2_920_650));

        engine.Apply(new DamageEvent(At(1), Caster, Boss, OwnSkill, 1_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Summon, Boss, SummonSkill, 2_000, HitModifiers.None));

        foreach (var snapshot in new[] { engine.CurrentSnapshot(), engine.SessionSnapshot(), engine.RunSnapshot() })
        {
            var actor = Assert.Single(snapshot!.Actors);
            Assert.Equal((3_000L, 2_000L), (actor.Damage, actor.SummonDamage));
        }
    }

    [Fact]
    public void A_player_without_summons_has_no_summon_damage()
    {
        var engine = CreateEngine();

        engine.Apply(new DamageEvent(At(1), Caster, Boss, OwnSkill, 1_000, HitModifiers.None));

        var actor = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal(0L, actor.SummonDamage);
        Assert.False(Assert.Single(actor.Skills).FromSummon);
    }

    private static CombatEngine CreateEngine()
    {
        var engine = new CombatEngine();
        engine.Apply(new LocalPlayerEvent(T0, Me, "Local"));
        engine.Apply(new EntityInfoEvent(T0, Caster, "Hechicera", 15));
        engine.Apply(new PartyMemberSeenEvent(T0, Caster));
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12_345));
        return engine;
    }

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);
}
