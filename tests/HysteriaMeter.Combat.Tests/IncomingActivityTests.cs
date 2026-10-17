using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat.Tests;

/// <summary>Eventos sintéticos de pausas de DPS durante las que los NPC siguen atacando.</summary>
public sealed class IncomingActivityTests
{
    private const ulong Me = 1;
    private const ulong Ally = 2;
    private const ulong Stranger = 3;
    private const ulong Npc = 900;
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Incoming_damage_keeps_one_encounter_across_a_pause_in_outgoing_damage(bool tickDuringPause, bool hitPartyMember)
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Outgoing(0, 100));
        var id = engine.CurrentSnapshot()!.Id;

        engine.Apply(Incoming(6, hitPartyMember ? Ally : Me));
        if (tickDuringPause)
        {
            engine.Tick(At(9));
        }

        // Recibir daño mantiene la pelea, pero no modifica el reloj que calcula el DPS.
        var duringPause = engine.CurrentSnapshot();
        Assert.NotNull(duringPause);
        Assert.Equal(id, duringPause.Id);
        Assert.Equal(Start, duringPause.End);
        Assert.Equal(TimeSpan.FromSeconds(1), duringPause.Duration);
        Assert.Equal(TimeSpan.Zero, duringPause.MainTargetDuration);
        Assert.Empty(finished);

        engine.Apply(Outgoing(10, 200));
        var snapshot = engine.CurrentSnapshot()!;
        Assert.Equal(id, snapshot.Id);
        Assert.Empty(finished);
        Assert.Equal(300, snapshot.TotalDamage);
        Assert.Equal(TimeSpan.FromSeconds(10), snapshot.Duration);
        Assert.Equal(TimeSpan.FromSeconds(10), snapshot.MainTargetDuration);
        var actor = Assert.Single(snapshot.Actors, row => row.Id == Me);
        Assert.Equal(300, actor.MainTargetDamage);
        Assert.Equal(30d, actor.Dps);
        Assert.Equal(30d, actor.MainTargetDps);
        Assert.Equal(50, snapshot.Actors.Sum(row => row.DamageTaken));
    }

    [Fact]
    public void Idle_expires_eight_seconds_after_the_last_accepted_incoming_hit()
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Outgoing(0, 100));
        engine.Apply(Incoming(6, Me));

        engine.Tick(At(14));
        Assert.NotNull(engine.CurrentSnapshot());
        Assert.Empty(finished);
        engine.Tick(At(14.001));

        Assert.Null(engine.CurrentSnapshot());
        var snapshot = Assert.Single(finished);
        Assert.Equal(100, snapshot.TotalDamage);
        Assert.Equal(Start, snapshot.End);
        Assert.Equal(TimeSpan.FromSeconds(1), snapshot.Duration);
    }

    [Theory]
    [InlineData("heal")]
    [InlineData("health")]
    [InlineData("party")]
    public void Non_damage_updates_do_not_keep_an_idle_encounter_open(string update)
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Outgoing(0, 100));
        CombatEvent activity = update switch
        {
            "heal" => new HealEvent(At(6), Me, Me, 20, 50),
            "health" => new HealthEvent(At(6), Npc, 100_000),
            _ => new PartyMemberSeenEvent(At(6), Ally),
        };
        engine.Apply(activity);
        engine.Tick(At(9));

        Assert.Null(engine.CurrentSnapshot());
        Assert.Equal(100, Assert.Single(finished).TotalDamage);
    }

    [Fact]
    public void Incoming_damage_outside_scope_does_not_keep_an_encounter_open()
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Outgoing(0, 100));
        engine.Apply(Incoming(6, Stranger));
        engine.Tick(At(9));

        Assert.Null(engine.CurrentSnapshot());
        var snapshot = Assert.Single(finished);
        Assert.DoesNotContain(snapshot.Actors, row => row.Id == Stranger);
        Assert.Equal(0, snapshot.Actors.Sum(row => row.DamageTaken));
    }

    [Fact]
    public void Incoming_damage_without_an_encounter_does_not_open_one()
    {
        var engine = CreateEngine();
        engine.Apply(Incoming(6, Me));
        engine.Tick(At(9));

        Assert.Null(engine.CurrentSnapshot());
        Assert.Null(engine.SessionSnapshot());
        Assert.Null(engine.RunSnapshot());
        Assert.Empty(engine.CombatLog());
    }

    [Fact]
    public void Late_incoming_damage_does_not_revive_an_expired_encounter_without_a_tick()
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Outgoing(0, 100));
        var id = engine.CurrentSnapshot()!.Id;

        engine.Apply(Incoming(20, Me));

        Assert.Null(engine.CurrentSnapshot());
        var snapshot = Assert.Single(finished);
        Assert.Equal(id, snapshot.Id);
        Assert.Equal(0, Assert.Single(snapshot.Actors).DamageTaken);
        Assert.DoesNotContain(engine.CombatLog(), line => line.Timestamp == At(20));

        engine.Apply(Outgoing(21, 200));
        Assert.NotEqual(id, engine.CurrentSnapshot()!.Id);
        Assert.Equal(200, engine.CurrentSnapshot()!.TotalDamage);
        Assert.Single(finished);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Out_of_order_damage_does_not_move_combat_activity_backwards(bool delayedOutgoing)
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Outgoing(0, 100));
        var id = engine.CurrentSnapshot()!.Id;
        engine.Apply(Incoming(6, Me));
        engine.Apply(delayedOutgoing ? Outgoing(4, 200) : Incoming(4, Me));
        engine.Tick(At(13));

        Assert.Empty(finished);
        Assert.NotNull(engine.CurrentSnapshot());
        Assert.Equal(id, engine.CurrentSnapshot()!.Id);

        engine.Tick(At(14.001));
        Assert.Null(engine.CurrentSnapshot());
        Assert.Single(finished);
    }

    [Fact]
    public void Main_target_death_closes_the_preserved_encounter_once()
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Outgoing(0, 100));
        var id = engine.CurrentSnapshot()!.Id;
        engine.Apply(Incoming(6, Me));
        engine.Tick(At(9));
        engine.Apply(new DeathEvent(At(10), Npc));
        engine.Tick(At(30));

        Assert.Null(engine.CurrentSnapshot());
        var snapshot = Assert.Single(finished);
        Assert.Equal(id, snapshot.Id);
        Assert.True(snapshot.TargetKilled);
        Assert.Equal(At(10), snapshot.End);
        Assert.Equal(100, snapshot.TotalDamage);
    }

    [Fact]
    public void Disabling_split_preserves_accumulation_across_incoming_and_outgoing_idle_gaps()
    {
        var engine = CreateEngine();
        engine.SplitEncounters = false;
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Outgoing(0, 100));
        var id = engine.CurrentSnapshot()!.Id;
        engine.Apply(Incoming(20, Me));
        engine.Tick(At(100));
        engine.Apply(Outgoing(110, 200));

        Assert.Empty(finished);
        var snapshot = engine.CurrentSnapshot()!;
        Assert.Equal(id, snapshot.Id);
        Assert.Equal(300, snapshot.TotalDamage);
        Assert.Equal(50, Assert.Single(snapshot.Actors).DamageTaken);
        Assert.Equal(TimeSpan.FromSeconds(110), snapshot.Duration);
    }

    private static CombatEngine CreateEngine()
    {
        var engine = new CombatEngine();
        engine.Apply(new LocalPlayerEvent(Start, Me, "Local"));
        engine.Apply(new EntityInfoEvent(Start, Ally, "Aliado", 15));
        engine.Apply(new PartyMemberSeenEvent(Start, Ally));
        engine.Apply(new EntityInfoEvent(Start, Stranger, "Otro", 12));
        // Sin catálogo ni HP: la actividad entrante debe bastar, como en la captura real.
        engine.Apply(new EntitySpawnEvent(Start, Npc, null, NpcId: 12345));
        return engine;
    }

    private static DateTimeOffset At(double seconds) => Start.AddSeconds(seconds);

    private static DamageEvent Outgoing(double seconds, long amount) =>
        new(At(seconds), Me, Npc, 10, amount, HitModifiers.None);

    private static DamageEvent Incoming(double seconds, ulong target) =>
        new(At(seconds), Npc, target, 500, 50, HitModifiers.None);
}
