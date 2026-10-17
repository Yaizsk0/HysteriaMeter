using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat.Tests;

// Eventos sintéticos: no contienen nombres ni IDs de entidades de capturas reales.
public class SummonAttributionTests
{
    private const ulong Me = 1;
    private const ulong Caster = 2;
    private const ulong OldCaster = 3;
    private const ulong UnknownSource = 40;
    private const ulong Summon = 50;
    private const ulong Boss = 900;
    private const uint BittercoldWind = 15_280_143;
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Historical_same_name_entities_do_not_block_the_only_current_party_or_local_identity(bool currentIsLocal)
    {
        var engine = CreateEngine();
        engine.Apply(new EntityInfoEvent(T0, OldCaster, "Hechicera", 15));
        engine.Apply(new PartyMemberSeenEvent(T0, OldCaster));
        engine.Apply(new EntityInfoEvent(At(20), Caster, "Hechicera", 15));
        if (currentIsLocal)
            engine.Apply(new LocalPlayerEvent(At(20), Caster, "Hechicera"));
        else
            engine.Apply(new PartyMemberSeenEvent(At(20), Caster));

        engine.Apply(Hit(20, 2_000));
        engine.Apply(Hit(21, 500, HitModifiers.DamageOverTime));

        Assert.False(engine.Entities.IsPartyMember(OldCaster));
        Assert.Equal(Caster, engine.Entities.Canonical(Summon));
        var actor = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal((Caster, 2_500L), (actor.Id, actor.Damage));
        var skill = Assert.Single(actor.Skills);
        Assert.Equal((BittercoldWind, 2_000L, 500L), (skill.SkillId, skill.DirectDamage, skill.PeriodicDamage));
    }

    [Fact]
    public void Unnamed_class_inferred_from_damage_does_not_block_a_real_player()
    {
        var engine = CreateEngine();
        // El daño puede llegar antes del spawn: ObserveSkill no prueba que sea un jugador.
        engine.Apply(new DamageEvent(At(1), UnknownSource, Boss, BittercoldWind, 100, HitModifiers.None));
        Assert.Null(engine.CurrentSnapshot());
        Assert.Equal(15, engine.Entities.Profile(UnknownSource).ClassId);
        Assert.Null(engine.Entities.Profile(UnknownSource).Name);
        engine.Apply(new EntityInfoEvent(At(2), Caster, "Hechicera", 15));
        engine.Apply(new PartyMemberSeenEvent(At(2), Caster));

        engine.Apply(Hit(3, 2_000));

        var actor = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal((Caster, 2_000L), (actor.Id, actor.Damage));
        Assert.Equal(Caster, engine.Entities.Canonical(Summon));
    }

    [Fact]
    public void An_unnamed_class_only_entity_is_not_enough_evidence_to_adopt_a_summon()
    {
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(1), UnknownSource, Boss, BittercoldWind, 100, HitModifiers.None));
        Assert.Null(engine.CurrentSnapshot());
        // Everyone evita que el filtro de party oculte una atribución incorrecta.
        engine.Scope = ActorScope.Everyone;

        engine.Apply(Hit(2, 2_000));

        Assert.Equal(Summon, engine.Entities.Canonical(Summon));
        Assert.Null(engine.CurrentSnapshot());
    }

    [Fact]
    public void Party_membership_is_player_evidence_even_before_the_name_arrives()
    {
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(1), UnknownSource, Boss, BittercoldWind, 100, HitModifiers.None));
        engine.Apply(new PartyMemberSeenEvent(At(2), UnknownSource));

        engine.Apply(Hit(3, 2_000));

        var actor = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal((UnknownSource, 2_000L), (actor.Id, actor.Damage));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Different_player_names_remain_ambiguous_even_when_scope_only_counts_me(bool otherIsPartyMember)
    {
        var engine = CreateEngine();
        engine.Scope = ActorScope.Me;
        engine.Apply(new EntityInfoEvent(T0, Me, "Local", 15));
        engine.Apply(new EntityInfoEvent(T0, Caster, "Hechicera", 15));
        if (otherIsPartyMember)
            engine.Apply(new PartyMemberSeenEvent(T0, Caster));

        engine.Apply(Hit(1, 2_000));

        Assert.Equal(Summon, engine.Entities.Canonical(Summon));
        Assert.Null(engine.CurrentSnapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Same_name_entities_remain_ambiguous_without_exactly_one_current_identity(bool bothAreCurrent)
    {
        var engine = CreateEngine();
        engine.Scope = ActorScope.Everyone;
        engine.Apply(new EntityInfoEvent(T0, OldCaster, "Hechicera", 15));
        engine.Apply(new EntityInfoEvent(T0, Caster, "Hechicera", 15));
        engine.Apply(new PartyMemberSeenEvent(T0, OldCaster));
        engine.Apply(new PartyMemberSeenEvent(T0, Caster));
        if (bothAreCurrent)
        {
            engine.Apply(new PartyMemberSeenEvent(At(20), OldCaster));
            engine.Apply(new PartyMemberSeenEvent(At(20), Caster));
        }

        engine.Apply(Hit(20, 2_000));

        Assert.Equal(Summon, engine.Entities.Canonical(Summon));
        Assert.Null(engine.CurrentSnapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_explicit_owner_wins_over_ambiguous_class_candidates(bool ownerByName)
    {
        var engine = CreateEngine();
        engine.Apply(new EntityInfoEvent(T0, Caster, "Hechicera", 15));
        engine.Apply(new PartyMemberSeenEvent(T0, Caster));
        engine.Apply(new EntityInfoEvent(T0, OldCaster, "OtraHechicera", 15));
        engine.Apply(new PartyMemberSeenEvent(T0, OldCaster));
        engine.Apply(new EntitySpawnEvent(T0, Summon, ownerByName ? null : OldCaster,
            NpcId: 2_920_650, OwnerName: ownerByName ? "OtraHechicera" : null));

        engine.Apply(Hit(1, 2_000));

        var actor = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal((OldCaster, 2_000L), (actor.Id, actor.Damage));
        Assert.Equal(OldCaster, engine.Entities.Canonical(Summon));
    }

    private static CombatEngine CreateEngine()
    {
        var engine = new CombatEngine();
        engine.Apply(new LocalPlayerEvent(T0, Me, "Local"));
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12_345));
        engine.Apply(new EntitySpawnEvent(T0, Summon, null, NpcId: 2_920_650));
        return engine;
    }

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private static DamageEvent Hit(double seconds, long amount, HitModifiers modifiers = HitModifiers.None) =>
        new(At(seconds), Summon, Boss, BittercoldWind, amount, modifiers);
}
