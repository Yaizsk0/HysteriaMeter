using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat.Tests;

/// <summary>Regresiones sintéticas de atribución: el daño pertenece al dueño; la muerte, a la entidad física.</summary>
public sealed class SummonDeathTests
{
    private const ulong Owner = 1;
    private const ulong Summon = 50;
    private const ulong Boss = 900;
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData("id")]
    [InlineData("nombre")]
    [InlineData("clase")]
    public void Summon_death_does_not_kill_its_owner_in_any_aggregate(string ownership)
    {
        var engine = new CombatEngine();
        engine.Apply(new LocalPlayerEvent(Start, Owner, "Local"));
        engine.Apply(new EntityInfoEvent(Start, Owner, "Local", 15));
        engine.Apply(new EntitySpawnEvent(Start, Boss, null, NpcId: 12345));
        engine.Apply(new EntitySpawnEvent(Start, Summon,
            OwnerId: ownership == "id" ? Owner : null,
            NpcId: 2920650,
            OwnerName: ownership == "nombre" ? "Local" : null));
        engine.Apply(new DamageEvent(Start, Owner, Boss, 15280143, 300, HitModifiers.None));
        engine.Apply(new DamageEvent(Start.AddSeconds(1), Summon, Boss, 15280143, 700, HitModifiers.None));
        Assert.Equal(1000, Assert.Single(engine.CurrentSnapshot()!.Actors).Damage);

        // El HP de la invocación llega a cero antes de su muerte, como en la captura real.
        engine.Apply(new HealthEvent(Start.AddSeconds(1.9), Summon, 0));
        engine.Apply(new DeathEvent(Start.AddSeconds(2), Summon));
        foreach (var snapshot in Snapshots(engine))
        {
            var actor = Assert.Single(snapshot.Actors);
            Assert.Equal(Owner, actor.Id);
            Assert.Equal(1000, actor.Damage);
            Assert.Equal(0, actor.Deaths);
            Assert.Empty(snapshot.Deaths ?? []);
        }

        Assert.DoesNotContain(engine.CombatLog(), line => line.Kind == LogKind.Death);

        // Una muerte posterior del jugador sí suma exactamente una, con el golpe y su recap correctos.
        engine.Apply(new DamageEvent(Start.AddSeconds(3), Boss, Owner, 500, 250, HitModifiers.None));
        engine.Apply(new DeathEvent(Start.AddSeconds(4), Owner));
        foreach (var snapshot in Snapshots(engine))
        {
            var actor = Assert.Single(snapshot.Actors);
            Assert.Equal(1, actor.Deaths);
            Assert.Equal(1000, actor.Damage);
            var recap = Assert.Single(snapshot.Deaths!);
            Assert.Equal(Owner, recap.ActorId);
            Assert.Equal(Boss, Assert.Single(recap.LastHits).SourceId);
            Assert.Equal(250, recap.KillingBlow!.Amount);
        }

        var death = Assert.Single(engine.CombatLog(), line => line.Kind == LogKind.Death);
        Assert.Equal((Owner, Owner), (death.SourceId, death.TargetId));
    }

    [Fact]
    public void Unclassified_combat_alias_keeps_the_existing_player_death_attribution()
    {
        var engine = new CombatEngine();
        engine.Apply(new LocalPlayerEvent(Start, Owner, "Local"));
        engine.Apply(new EntitySpawnEvent(Start, Boss, null, NpcId: 12345));
        engine.Apply(new EntitySpawnEvent(Start, Summon, OwnerId: Owner, NpcId: null));
        engine.Apply(new DamageEvent(Start, Summon, Boss, 15280143, 100, HitModifiers.None));
        engine.Apply(new DeathEvent(Start.AddSeconds(1), Summon));

        // Sin clasificación de NPC, puede ser una entidad de combate transitoria del propio jugador.
        foreach (var snapshot in Snapshots(engine))
        {
            Assert.Equal(1, Assert.Single(snapshot.Actors).Deaths);
            Assert.Equal(Owner, Assert.Single(snapshot.Deaths!).ActorId);
        }

        Assert.False(engine.Entities.IsDeadNpc(Owner));
    }
    [Fact]
    public void Owned_npc_death_marks_its_body_dead_and_respawn_clears_only_that_body()
    {
        var entities = new EntityRegistry();
        entities.Apply(new LocalPlayerEvent(Start, Owner, "Local"));
        var spawn = new EntitySpawnEvent(Start, Summon, OwnerId: Owner, NpcId: 2920650);
        entities.Apply(spawn);
        entities.Apply(new DeathEvent(Start.AddSeconds(1), Summon));

        Assert.True(entities.IsDeadNpc(Summon));
        Assert.False(entities.IsDeadNpc(Owner));
        Assert.Equal(Owner, entities.Canonical(Summon));

        entities.Apply(spawn with { Timestamp = Start.AddSeconds(2) });
        Assert.False(entities.IsDeadNpc(Summon));
        Assert.Equal(Owner, entities.Canonical(Summon));
    }

    private static EncounterSnapshot[] Snapshots(CombatEngine engine) =>
        [engine.CurrentSnapshot()!, engine.SessionSnapshot()!, engine.RunSnapshot()!];
}
