using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat.Tests;

public class CombatEngineTests
{
    private const ulong Me = 1;
    private const ulong Ally = 2;
    private const ulong MySummon = 50;
    private const ulong Boss = 900;

    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 21, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private static CombatEngine CreateEngine()
    {
        var engine = new CombatEngine();
        engine.Apply(new LocalPlayerEvent(T0, Me, "Mati"));
        engine.Apply(new EntityInfoEvent(T0, Ally, "Aliado", 3));
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345));
        engine.Apply(new EntityInfoEvent(T0, Boss, "Boss", null));
        return engine;
    }

    [Fact]
    public void Computes_dps_and_share_per_actor()
    {
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Me, Boss, 10, 1000, HitModifiers.Critical));
        engine.Apply(new DamageEvent(At(5), Ally, Boss, 20, 500, HitModifiers.None));
        engine.Apply(new DamageEvent(At(10), Me, Boss, 11, 1500, HitModifiers.None));

        var snapshot = engine.CurrentSnapshot()!;

        Assert.Equal(TimeSpan.FromSeconds(10), snapshot.Duration);
        Assert.Equal("Boss", snapshot.TargetName);
        var me = snapshot.Actors[0];
        Assert.Equal(("Mati", true, 2500L, 250d), (me.Name, me.IsLocalPlayer, me.Damage, me.Dps));
        Assert.Equal(2500d / 3000, me.Share, 3);
        Assert.Equal(0.5, me.CritRate);
        Assert.Equal([11U, 10U], me.Skills.Select(s => s.SkillId));
        Assert.Equal("Aliado", snapshot.Actors[1].Name);
    }

    [Fact]
    public void Summon_damage_is_attributed_to_owner()
    {
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, MySummon, OwnerId: Me, NpcId: null));
        engine.Apply(new DamageEvent(At(0), MySummon, Boss, 99, 700, HitModifiers.None));

        var actor = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal(Me, actor.Id);
    }

    [Fact]
    public void Summon_with_owner_name_is_attributed_once_the_owner_name_is_known()
    {
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, MySummon, OwnerId: null, NpcId: 2920650, OwnerName: "Aliado"));
        engine.Apply(new EntityInfoEvent(T0, Ally, "Aliado", null));
        engine.Apply(new DamageEvent(At(0), MySummon, Boss, 99, 700, HitModifiers.None));

        var actor = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal((Ally, 700L), (actor.Id, actor.Damage));
    }

    [Fact]
    public void Summon_with_unknown_owner_is_not_ranked()
    {
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, MySummon, OwnerId: null, NpcId: 2920650, OwnerName: "Nadie"));
        engine.Apply(new DamageEvent(At(0), MySummon, Boss, 99, 700, HitModifiers.None));

        Assert.Null(engine.CurrentSnapshot());
    }

    [Fact]
    public void Dot_ticks_add_damage_but_not_hits()
    {
        // Punishment en Combat Analysis: 2 golpes y 191.141 de daño, contando el DoT.
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Me, Boss, 7, 66_000, HitModifiers.MultiHit | HitModifiers.FrontAttack));
        engine.Apply(new DamageEvent(At(1), Me, Boss, 7, 66_263, HitModifiers.Critical));
        engine.Apply(new DamageEvent(At(2), Me, Boss, 7, 58_878, HitModifiers.DamageOverTime));

        var skill = Assert.Single(Assert.Single(engine.CurrentSnapshot()!.Actors).Skills);
        Assert.Equal((191_141L, 2, 1, 66_263L), (skill.Damage, skill.Hits, skill.Crits, skill.MaxHit));
    }

    [Fact]
    public void Main_target_damage_and_window_match_combat_analysis()
    {
        // Combat Analysis mide solo el objetivo elegido, del primer al último golpe sobre él.
        const ulong add = 901;
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, add, null, NpcId: 777));
        engine.Apply(new DamageEvent(At(0), Me, add, 1, 500, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Me, Boss, 1, 10_000, HitModifiers.Critical | HitModifiers.BackAttack));
        engine.Apply(new DamageEvent(At(4), Me, add, 1, 500, HitModifiers.None));
        engine.Apply(new DamageEvent(At(7), Me, Boss, 1, 10_000, HitModifiers.MultiHit | HitModifiers.FrontAttack));

        var snapshot = engine.CurrentSnapshot()!;
        var me = Assert.Single(snapshot.Actors);
        Assert.Equal(TimeSpan.FromSeconds(5), snapshot.MainTargetDuration);
        Assert.Equal((21_000L, 20_000L, 4_000d), (me.Damage, me.MainTargetDamage, me.MainTargetDps));
        Assert.Equal(new HitCounts(4, 1, 0, 0, 1, 1, 1), me.Counts);
    }

    [Fact]
    public void Npc_damage_is_not_ranked()
    {
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Boss, Me, 1, 5000, HitModifiers.None));
        Assert.Null(engine.CurrentSnapshot());
    }

    [Fact]
    public void Idle_gap_starts_a_new_encounter()
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;

        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 100, HitModifiers.None));
        engine.Apply(new DamageEvent(At(30), Me, Boss, 1, 200, HitModifiers.None));

        Assert.Equal(100, Assert.Single(finished).TotalDamage);
        Assert.Equal(200, engine.CurrentSnapshot()!.TotalDamage);
    }

    [Fact]
    public void Tick_closes_idle_encounter()
    {
        var engine = CreateEngine();
        EncounterSnapshot? finished = null;
        engine.EncounterFinished += s => finished = s;

        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 100, HitModifiers.None));
        engine.Tick(At(5));
        Assert.Null(finished);
        engine.Tick(At(9));
        Assert.NotNull(finished);
        Assert.Null(engine.CurrentSnapshot());
    }

    [Fact]
    public void Main_target_death_ends_encounter_as_kill()
    {
        var engine = CreateEngine();
        EncounterSnapshot? finished = null;
        engine.EncounterFinished += s => finished = s;

        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 100, HitModifiers.None));
        engine.Apply(new DeathEvent(At(3), Boss));

        Assert.True(finished!.TargetKilled);
        Assert.Equal(TimeSpan.FromSeconds(3), finished.Duration);
    }

    [Fact]
    public void Healing_is_tracked_during_encounter()
    {
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 100, HitModifiers.None));
        engine.Apply(new HealEvent(At(1), Ally, Me, 7, 300));

        Assert.Equal(300, engine.CurrentSnapshot()!.Actors.Single(a => a.Id == Ally).Healing);
    }
}
