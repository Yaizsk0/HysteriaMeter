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

    [Fact]
    public void The_owner_id_of_the_spawn_wins_over_the_class_even_before_the_owner_is_seen()
    {
        // El dueño de la cola del 0x3641 puede no haber aparecido todavía: igual es él, no el único jugador conocido de
        // la clase.
        var engine = CreateEngine();
        engine.Scope = ActorScope.Everyone;
        engine.Apply(new EntityInfoEvent(T0, Caster, "Hechicera", 15));
        engine.Apply(new PartyMemberSeenEvent(T0, Caster));
        engine.Apply(new EntitySpawnEvent(T0, Summon, OldCaster, NpcId: 2_920_650));

        engine.Apply(Hit(1, 2_000));

        var actor = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal((OldCaster, 2_000L), (actor.Id, actor.Damage));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("nombre")]
    [InlineData("clase")]
    public void A_respawned_id_forgets_its_previous_owner(string previousOwner)
    {
        var engine = CreateEngine();
        engine.Apply(new EntityInfoEvent(T0, Caster, "Hechicera", 15));
        engine.Apply(new PartyMemberSeenEvent(T0, Caster));
        switch (previousOwner)
        {
            case "id":
                engine.Apply(new EntitySpawnEvent(T0, Summon, Caster, NpcId: 2_920_650));
                break;
            case "nombre":
                engine.Apply(new EntitySpawnEvent(T0, Summon, null, NpcId: 2_920_650, OwnerName: "Hechicera"));
                break;
            default:
                engine.Apply(Hit(1, 2_000)); // adoptada por la clase
                break;
        }

        Assert.Equal(Caster, engine.Entities.Canonical(Summon));

        // El juego reusa el id: para la invocación de otro jugador y después para un monstruo.
        engine.Apply(new EntitySpawnEvent(At(10), Summon, OldCaster, NpcId: 2_920_650));
        Assert.Equal(OldCaster, engine.Entities.Canonical(Summon));
        engine.Apply(new EntitySpawnEvent(At(20), Summon, null, NpcId: 12_346));
        Assert.Equal(Summon, engine.Entities.Canonical(Summon));
    }

    [Fact]
    public void A_summon_hitting_before_its_spawn_is_not_ranked_but_the_verification_counts_it()
    {
        // Antes de su 0x3641 la invocación no tiene npcId. Pega con una skill que hasta ahí solo usaron invocaciones, así
        // que no es un jugador; con dos jugadores de su clase no se adopta, pero le baja la vida al boss.
        var engine = CreateEngine();
        engine.Scope = ActorScope.Everyone;
        engine.Apply(new EntityInfoEvent(T0, Caster, "Hechicera", 15));
        engine.Apply(new EntityInfoEvent(T0, OldCaster, "OtraHechicera", 15));
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12_345, Hp: 10_000, MaxHp: 10_000));
        engine.Apply(new DamageEvent(At(1), Me, Boss, 1, 1_000, HitModifiers.None));
        engine.Apply(Hit(2, 2_000));
        engine.Apply(new DamageEvent(At(3), UnknownSource, Boss, BittercoldWind, 3_000, HitModifiers.None));
        engine.Apply(new HealthEvent(At(3), Boss, 4_000));

        var snapshot = engine.CurrentSnapshot()!;
        Assert.Equal([(Me, 1_000L)], snapshot.Actors.Select(a => (a.Id, a.Damage)));
        var verification = Assert.Single(snapshot.Verifications!);
        Assert.Equal((6_000L, true), (verification.Counted, verification.Closes));
    }

    [Fact]
    public void A_summon_hitting_before_its_spawn_goes_to_the_only_player_of_its_class_until_the_spawn_says_otherwise()
    {
        var engine = CreateEngine();
        engine.Apply(new EntityInfoEvent(T0, Caster, "Hechicera", 15));
        engine.Apply(new PartyMemberSeenEvent(T0, Caster));
        engine.Apply(Hit(1, 2_000));

        engine.Apply(new DamageEvent(At(2), UnknownSource, Boss, BittercoldWind, 3_000, HitModifiers.None));

        var actor = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal((Caster, 5_000L), (actor.Id, actor.Damage));
        Assert.Equal(Caster, engine.Entities.Canonical(UnknownSource));

        // Su spawn trae otro dueño en la cola: manda sobre la adopción.
        engine.Apply(new EntitySpawnEvent(At(3), UnknownSource, OldCaster, NpcId: 2_920_655));
        Assert.Equal(OldCaster, engine.Entities.Canonical(UnknownSource));
    }

    [Fact]
    public void A_skill_a_known_player_used_does_not_make_an_unknown_source_a_summon()
    {
        var engine = CreateEngine();
        engine.Scope = ActorScope.Everyone;
        engine.Apply(new EntityInfoEvent(T0, Caster, "Hechicera", 15));
        engine.Apply(new EntityInfoEvent(T0, OldCaster, "OtraHechicera", 15));
        engine.Apply(Hit(1, 2_000));
        engine.Apply(new DamageEvent(At(2), Caster, Boss, BittercoldWind, 1_000, HitModifiers.None));

        engine.Apply(new DamageEvent(At(3), UnknownSource, Boss, BittercoldWind, 3_000, HitModifiers.None));

        Assert.Equal([(Caster, 1_000L), (UnknownSource, 3_000L)],
            engine.CurrentSnapshot()!.Actors.Select(a => (a.Id, a.Damage)).OrderBy(a => a.Id));
    }

    [Fact]
    public void Hits_between_a_player_and_its_own_summon_are_neither_damage_nor_damage_taken()
    {
        // La invocación que se pega a sí misma al vencer (su vida más 100.000), la que le pega a su dueño y el dueño que
        // le pega a su invocación: con el dueño resuelto son de un jugador a sí mismo.
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, Summon, Me, NpcId: 2_920_113));
        engine.Apply(new DamageEvent(At(1), Me, Boss, 1, 1_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Summon, Summon, 16_990_004, 114_071, HitModifiers.None));
        engine.Apply(new DamageEvent(At(3), Summon, Me, 1_600_865, 2_363, HitModifiers.None));
        engine.Apply(new DamageEvent(At(4), Me, Summon, 16_770_001, 300, HitModifiers.None));

        var actor = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal((Me, 1_000L, 0L), (actor.Id, actor.Damage, actor.DamageTaken));
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
