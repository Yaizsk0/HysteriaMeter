using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat.Tests;

/// <summary>
/// Una pelea de boss (marcado en el catálogo de NPCs) no se corta en una pausa de fase mientras el boss siga vivo y sin
/// resetear. Ferocious Horn Nuakum quedaba en dos peleas por pausas de 8,3 a 8,6 s sin daño hecho (2026-10-19 y
/// 2026-10-29); en la del 2026-10-29, la segunda arrancaba con la misma vida del boss con que terminaba la primera y se
/// llevaba el kill.
/// </summary>
public sealed class BossPauseTests
{
    private const ulong Me = 1;
    private const ulong Boss = 900;
    private const ulong Mob = 901;
    private const ulong OtherBoss = 902;
    private const ulong Respawn = 903;
    private const ulong Helper = 904;
    private const uint BossNpc = 2_300_910;
    private const uint OtherBossNpc = 2_300_371;
    private const uint HelperNpc = 2_920_445;
    private const uint TrashNpc = 2_300_906;
    private const uint UnknownNpc = 2_399_999;
    private const long MaxHp = 15_504_000;
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData(8.3, false)]
    [InlineData(8.3, true)]
    [InlineData(29, false)]
    [InlineData(29, true)]
    public void A_phase_pause_does_not_split_a_boss_fight(double pause, bool tickDuringPause)
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Hit(0, Boss, 100));
        engine.Apply(new HealthEvent(At(0.1), Boss, MaxHp - 100));
        var id = engine.CurrentSnapshot()!.Id;
        if (tickDuringPause)
        {
            engine.Tick(At(8.1));
            engine.Tick(At(pause));
        }

        engine.Apply(Hit(pause, Boss, 200));
        engine.Apply(new DeathEvent(At(pause + 1), Boss));

        var fight = Assert.Single(finished);
        Assert.Equal(id, fight.Id);
        Assert.True(fight.TargetKilled);
        Assert.Equal(300, fight.TotalDamage);
        Assert.Equal(TimeSpan.FromSeconds(pause + 1), fight.Duration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_boss_fight_closes_after_the_boss_pause_without_damage(bool tick)
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Hit(0, Boss, 100));
        var id = engine.CurrentSnapshot()!.Id;

        engine.Tick(At(30));
        Assert.NotNull(engine.CurrentSnapshot());
        if (tick)
        {
            engine.Tick(At(30.001));
            Assert.Null(engine.CurrentSnapshot());
        }

        // Sin un Tick intermedio, el golpe tardío tampoco revive la pelea vencida.
        engine.Apply(Hit(31, Boss, 200));

        var first = Assert.Single(finished);
        Assert.Equal(id, first.Id);
        Assert.Equal(100, first.TotalDamage);
        Assert.Equal(Start, first.End);
        Assert.NotEqual(id, engine.CurrentSnapshot()!.Id);
        Assert.Equal(200, engine.CurrentSnapshot()!.TotalDamage);
    }

    // Wipe: el boss vuelve a la vida máxima (una lectura nueva o un spawn con vida) y la pelea cierra con la inactividad de
    // siempre, para que el próximo intento sea otra pelea.
    [Theory]
    [InlineData("health")]
    [InlineData("spawn")]
    public void A_boss_back_at_full_health_ends_the_fight_at_the_normal_idle(string reset)
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Hit(0, Boss, 100));
        engine.Apply(new HealthEvent(At(0.1), Boss, MaxHp - 100));
        var id = engine.CurrentSnapshot()!.Id;
        engine.Apply(reset == "health"
            ? new HealthEvent(At(5), Boss, MaxHp)
            : new EntitySpawnEvent(At(5), Boss, null, BossNpc, Hp: MaxHp, MaxHp: MaxHp));

        engine.Tick(At(8));
        Assert.NotNull(engine.CurrentSnapshot());
        engine.Tick(At(8.001));

        Assert.Null(engine.CurrentSnapshot());
        var wipe = Assert.Single(finished);
        Assert.Equal(id, wipe.Id);
        Assert.False(wipe.TargetKilled);
        engine.Apply(Hit(20, Boss, 200));
        Assert.NotEqual(id, engine.CurrentSnapshot()!.Id);
    }

    // Al hacer el pull, la lectura de vida llena puede llegar después del primer golpe: no es un reset, porque en esta
    // pelea el boss todavía no se vio herido.
    [Fact]
    public void Full_health_before_the_boss_is_hurt_is_not_a_reset()
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Hit(0, Boss, 100));
        engine.Apply(new HealthEvent(At(0.05), Boss, MaxHp));

        engine.Tick(At(9));

        Assert.NotNull(engine.CurrentSnapshot());
        Assert.Empty(finished);
    }

    // Si el boss vuelve a bajar después de una lectura llena, la pelea siguió: vuelve a esperar la pausa de boss.
    [Fact]
    public void A_boss_hurt_again_after_full_health_keeps_the_boss_pause()
    {
        var engine = CreateEngine();
        engine.Apply(Hit(0, Boss, 100));
        engine.Apply(new HealthEvent(At(0.1), Boss, MaxHp - 100));
        engine.Apply(new HealthEvent(At(1), Boss, MaxHp));
        engine.Apply(Hit(2, Boss, 100));
        engine.Apply(new HealthEvent(At(2.1), Boss, MaxHp - 100));

        engine.Tick(At(20));

        Assert.NotNull(engine.CurrentSnapshot());
    }

    // Atiel (2026-10-20 y 21) vuelve como otra entidad en cada intento, con la vida llena, y de la vieja no llega ni la
    // muerte ni una lectura llena: el spawn de la nueva es el reset. Sin vida en el spawn también.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_boss_that_comes_back_as_another_entity_ends_the_boss_pause(bool spawnWithHealth)
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Hit(0, Boss, 100));
        engine.Apply(new HealthEvent(At(0.1), Boss, MaxHp - 100));
        var id = engine.CurrentSnapshot()!.Id;
        engine.Apply(spawnWithHealth
            ? new EntitySpawnEvent(At(5), Respawn, null, BossNpc, Hp: MaxHp, MaxHp: MaxHp)
            : new EntitySpawnEvent(At(5), Respawn, null, BossNpc));

        engine.Tick(At(8));
        Assert.NotNull(engine.CurrentSnapshot());
        engine.Tick(At(8.001));

        Assert.Null(engine.CurrentSnapshot());
        var wipe = Assert.Single(finished);
        Assert.Equal(id, wipe.Id);
        Assert.False(wipe.TargetKilled);
        engine.Apply(Hit(24, Respawn, 200));
        Assert.NotEqual(id, engine.CurrentSnapshot()!.Id);
    }

    // Una entidad del mismo NPC que llega herida no es el boss que vuelve: no corta la pausa.
    [Fact]
    public void A_hurt_copy_of_the_boss_keeps_the_boss_pause()
    {
        var engine = CreateEngine();
        engine.Apply(Hit(0, Boss, 100));
        engine.Apply(new EntitySpawnEvent(At(5), Respawn, null, BossNpc, Hp: MaxHp / 2, MaxHp: MaxHp));

        engine.Tick(At(20));

        Assert.NotNull(engine.CurrentSnapshot());
    }

    // Pasada la inactividad configurada, otro boss no es una pausa sino otra pelea. El 2026-10-29, Silver Blade Rotan
    // quedó vivo 13,8 s antes del pull de Kromede's Desire: si no se reseteó, ese pull se sumaba a su pelea.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hitting_another_boss_after_the_idle_timeout_starts_a_new_fight(bool tick)
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Hit(0, Boss, 100));
        engine.Apply(new HealthEvent(At(0.1), Boss, MaxHp - 100));
        var id = engine.CurrentSnapshot()!.Id;
        if (tick)
        {
            engine.Tick(At(13.5));
            Assert.NotNull(engine.CurrentSnapshot());
        }

        engine.Apply(Hit(13.8, OtherBoss, 200));

        var first = Assert.Single(finished);
        Assert.Equal(id, first.Id);
        Assert.Equal(100, first.TotalDamage);
        Assert.Equal(Start, first.End);
        var current = engine.CurrentSnapshot()!;
        Assert.NotEqual(id, current.Id);
        Assert.Equal(At(13.8), current.Start);
        Assert.Equal(200, current.TotalDamage);
    }

    // Si el otro boss hace el pull, sus golpes en la pausa no sostienen la pelea vieja: el primer golpe del grupo abre otra.
    [Fact]
    public void Another_boss_that_pulls_the_party_starts_a_new_fight_with_the_first_hit()
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Hit(0, Boss, 100));
        var id = engine.CurrentSnapshot()!.Id;

        engine.Apply(Incoming(12, OtherBoss));
        Assert.Equal(id, engine.CurrentSnapshot()!.Id);
        engine.Apply(Hit(13, OtherBoss, 200));

        Assert.Equal(id, Assert.Single(finished).Id);
        Assert.NotEqual(id, engine.CurrentSnapshot()!.Id);
    }

    // Pasada la inactividad, un NPC que no es parte de la pelea y le pega a la party no la corta ni la sostiene; la sostiene
    // la pausa de boss. Las entidades de mecánica de Atiel (npc 2920445 y 2920446) están marcadas boss en la copia del
    // usuario y pegan sin que nadie les pegue; los adds de otro boss que hace el pull pueden no estar en el catálogo.
    [Theory]
    [InlineData(Helper)]
    [InlineData(Mob)]
    public void An_npc_outside_the_fight_hitting_the_party_neither_ends_nor_sustains_the_boss_pause(ulong source)
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(new EntitySpawnEvent(Start, Mob, null, UnknownNpc));
        engine.Apply(Hit(0, Boss, 100));
        engine.Apply(new HealthEvent(At(0.1), Boss, MaxHp - 100));
        var id = engine.CurrentSnapshot()!.Id;

        engine.Tick(At(8.5));
        engine.Apply(Incoming(9, source));
        Assert.Equal(id, engine.CurrentSnapshot()!.Id);
        engine.Tick(At(30));
        Assert.NotNull(engine.CurrentSnapshot());
        engine.Tick(At(30.001));

        Assert.Equal(id, Assert.Single(finished).Id);
    }

    // Pasada la inactividad, lo que le pasa a un NPC que no es parte de la pelea (un golpe de un jugador fuera del filtro o
    // de una invocación sin dueño, una lectura de vida o su muerte) no entra en su verificación, y su cuenta queda
    // congelada: si la pelea sigue, lo que venga después (golpes, lecturas, su muerte) no se vería entero.
    [Theory]
    [InlineData("hit")]
    [InlineData("summon")]
    [InlineData("health")]
    [InlineData("death")]
    public void An_npc_outside_the_fight_keeps_a_consistent_verification_through_the_boss_pause(string pauseEvent)
    {
        const ulong stranger = 3;
        const ulong summon = 905;
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        // Dos jugadores de la clase de la invocación: no se le adopta dueño.
        engine.Apply(new EntityInfoEvent(Start, Me, "Local", 15));
        engine.Apply(new EntityInfoEvent(Start, stranger, "Otro", 15));
        engine.Apply(new EntitySpawnEvent(Start, summon, OwnerId: null, NpcId: 2_920_735));
        engine.Apply(Hit(0, Boss, 100));
        engine.Apply(new HealthEvent(At(0.1), Boss, MaxHp - 100));
        engine.Apply(new DamageEvent(At(3), stranger, OtherBoss, 10, 1_000, HitModifiers.None));
        engine.Apply(new HealthEvent(At(3.1), OtherBoss, MaxHp - 1_000));
        engine.Apply(pauseEvent switch
        {
            "hit" => new DamageEvent(At(10), stranger, OtherBoss, 10, 5_000, HitModifiers.None),
            "summon" => new DamageEvent(At(10), summon, OtherBoss, 15_200_023, 5_000, HitModifiers.None),
            "health" => new HealthEvent(At(10.1), OtherBoss, MaxHp - 6_000),
            _ => new DeathEvent(At(10.2), OtherBoss),
        });

        engine.Apply(Hit(12, Boss, 100));
        engine.Apply(new DamageEvent(At(13), stranger, OtherBoss, 10, 2_000, HitModifiers.None));
        engine.Apply(new HealthEvent(At(13.1), OtherBoss, MaxHp - 8_000));
        engine.Apply(new DeathEvent(At(13.2), OtherBoss));
        engine.Tick(At(42.001));

        var other = Assert.Single(Assert.Single(finished).Verifications!, v => v.TargetId == OtherBoss);
        Assert.Equal(MaxHp - 1_000, other.EndHp);
        Assert.Equal(1_000, other.Counted);
        Assert.True(other.Closes);
    }

    // Antes de la inactividad configurada nada cambia: dos bosses a la vez son una sola pelea. Y un boss que ya recibió
    // daño en esta pelea es parte de ella también pasada esa inactividad.
    [Theory]
    [InlineData(5, false)]
    [InlineData(12, true)]
    public void A_boss_that_is_part_of_the_fight_keeps_it(double second, bool hitBefore)
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(Hit(0, Boss, 100));
        if (hitBefore)
        {
            engine.Apply(Hit(0.5, OtherBoss, 50));
        }

        var id = engine.CurrentSnapshot()!.Id;

        engine.Apply(Hit(second, OtherBoss, 20));

        Assert.Equal(id, engine.CurrentSnapshot()!.Id);
        Assert.Empty(finished);
    }

    // Un add u otro boss que ya es parte de la pelea la sigue en la pausa, si se le pega o si pega: los 30 s se cuentan
    // desde ahí.
    [Theory]
    [InlineData(Mob, false)]
    [InlineData(Mob, true)]
    [InlineData(OtherBoss, false)]
    [InlineData(OtherBoss, true)]
    public void An_add_or_boss_that_is_part_of_the_fight_keeps_the_boss_pause(ulong npc, bool incoming)
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(new EntitySpawnEvent(Start, Mob, null, TrashNpc));
        engine.Apply(Hit(0, Boss, 100));
        engine.Apply(Hit(1, npc, 20));
        var id = engine.CurrentSnapshot()!.Id;

        engine.Apply(incoming ? Incoming(12, npc) : Hit(12, npc, 20));
        engine.Tick(At(41.9));
        Assert.Equal(id, engine.CurrentSnapshot()!.Id);
        engine.Tick(At(42.001));

        Assert.Null(engine.CurrentSnapshot());
        Assert.Equal(id, Assert.Single(finished).Id);
    }

    // Pegarle a un NPC nuevo pasada la inactividad abre otra pelea, como antes: en las pausas medidas (Nuakum, Kwapo,
    // Duanka), el grupo volvió a pegarle al boss. Si no, los adds de otro boss que hace el pull se sumaban a la pelea vieja.
    [Fact]
    public void Hitting_a_new_add_after_the_idle_timeout_starts_a_new_fight()
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(new EntitySpawnEvent(Start, Mob, null, TrashNpc));
        engine.Apply(Hit(0, Boss, 100));
        var id = engine.CurrentSnapshot()!.Id;

        engine.Apply(Hit(12, Mob, 20));

        Assert.Equal(id, Assert.Single(finished).Id);
        Assert.NotEqual(id, engine.CurrentSnapshot()!.Id);
        Assert.Equal(20, engine.CurrentSnapshot()!.TotalDamage);
    }

    // En la pausa, un golpe del filtro que no puede abrir una pelea (al cadáver de un NPC que no es parte de ella, o un costo
    // de vida propio) se descarta: no corta la pelea de boss ni abre una fantasma.
    [Theory]
    [InlineData("corpse")]
    [InlineData("self")]
    public void A_hit_that_cannot_open_a_fight_does_not_end_the_boss_pause(string hit)
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(new EntitySpawnEvent(Start, Mob, null, TrashNpc));
        engine.Apply(Hit(0, Boss, 100_000));
        engine.Apply(new DeathEvent(At(5), Mob));
        var id = engine.CurrentSnapshot()!.Id;

        engine.Apply(hit == "corpse" ? Hit(10, Mob, 20) : new DamageEvent(At(10), Me, Me, 10, 884, HitModifiers.None));

        Assert.Empty(finished);
        Assert.Equal(id, engine.CurrentSnapshot()!.Id);
        Assert.Equal(100_000, engine.CurrentSnapshot()!.TotalDamage);
    }

    // El "daño" de un jugador a sí mismo (un costo de vida) entra en la pelea viva, pero no hace al jugador parte de ella:
    // en la pausa no la sigue ni la estira, y se descarta, así que pegarle después a un NPC nuevo abre otra pelea.
    [Fact]
    public void Damage_between_players_does_not_keep_the_boss_pause()
    {
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(new EntitySpawnEvent(Start, Mob, null, TrashNpc));
        engine.Apply(Hit(0, Boss, 100_000));
        engine.Apply(new DamageEvent(At(1), Me, Me, 10, 884, HitModifiers.None));
        var id = engine.CurrentSnapshot()!.Id;

        engine.Apply(new DamageEvent(At(12), Me, Me, 10, 884, HitModifiers.None));
        Assert.Equal(id, engine.CurrentSnapshot()!.Id);
        engine.Apply(Hit(13, Mob, 20));

        var first = Assert.Single(finished);
        Assert.Equal(id, first.Id);
        Assert.Equal(At(1), first.End);
        Assert.NotEqual(id, engine.CurrentSnapshot()!.Id);
    }

    // La muerte del objetivo principal cierra la pelea con ese mismo evento: su verificación registra la muerte aunque llegue
    // en la pausa de boss (el kill lo hacen jugadores fuera del filtro, o una mecánica). El registro ya lo marcó muerto, y un
    // boss muerto no sostiene la pausa.
    [Fact]
    public void The_main_target_dying_in_the_boss_pause_is_a_kill_in_its_verification()
    {
        const ulong stranger = 3;
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(new EntityInfoEvent(Start, stranger, "Otro", 12));
        engine.Apply(Hit(0, Boss, 1_000));
        engine.Apply(new HealthEvent(At(0.1), Boss, MaxHp - 1_000));
        engine.Apply(new DamageEvent(At(15), stranger, Boss, 10, MaxHp - 1_000, HitModifiers.None));
        engine.Apply(new DeathEvent(At(15.1), Boss));

        var fight = Assert.Single(finished);
        Assert.True(fight.TargetKilled);
        var boss = Assert.Single(fight.Verifications!, v => v.TargetId == Boss);
        Assert.Equal(0, boss.EndHp);
        Assert.Equal(MaxHp, boss.Counted);
        Assert.True(boss.Closes);
    }

    // Que aparezca otro boss en la pausa no es un reset: el reset por reaparición es solo del mismo NPC.
    [Fact]
    public void A_spawn_of_another_boss_is_not_a_reset()
    {
        var engine = CreateEngine();
        engine.Apply(Hit(0, Boss, 100));
        engine.Apply(new HealthEvent(At(0.1), Boss, MaxHp - 100));
        engine.Apply(new EntitySpawnEvent(At(5), Respawn, null, OtherBossNpc, Hp: MaxHp, MaxHp: MaxHp));

        engine.Tick(At(20));

        Assert.NotNull(engine.CurrentSnapshot());
    }

    // Trash y monstruos sin catálogo (como el replay de las peleas de referencia): la inactividad de siempre.
    [Theory]
    [InlineData(TrashNpc)]
    [InlineData(UnknownNpc)]
    public void A_fight_against_something_that_is_not_a_boss_still_closes_at_the_idle_timeout(uint npc)
    {
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(Start, Mob, null, npc));
        engine.Apply(Hit(0, Mob, 100));

        engine.Tick(At(8));
        Assert.NotNull(engine.CurrentSnapshot());
        engine.Tick(At(8.001));

        Assert.Null(engine.CurrentSnapshot());
    }

    // El objetivo principal es el que más daño recibió: si es un add, la pelea no es de boss.
    [Fact]
    public void A_fight_whose_main_target_is_not_the_boss_closes_at_the_idle_timeout()
    {
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(Start, Mob, null, TrashNpc));
        engine.Apply(Hit(0, Boss, 100));
        engine.Apply(Hit(1, Mob, 500));

        engine.Tick(At(9.001));

        Assert.Null(engine.CurrentSnapshot());
    }

    [Fact]
    public void A_longer_configured_idle_timeout_still_wins_over_the_boss_pause()
    {
        var engine = CreateEngine();
        engine.IdleTimeout = TimeSpan.FromSeconds(60);
        engine.Apply(Hit(0, Boss, 100));

        engine.Tick(At(45));

        Assert.NotNull(engine.CurrentSnapshot());
    }

    private static CombatEngine CreateEngine()
    {
        var engine = new CombatEngine();
        engine.Entities.Npcs = new NpcCatalog(new Dictionary<uint, NpcInfo>
        {
            [BossNpc] = new("Ferocious Horn Nuakum", "Ferocious Horn Den", IsBoss: true),
            [OtherBossNpc] = new("Kromede's Desire", "Fire Temple", IsBoss: true),
            [HelperNpc] = new("Atiel", null, IsBoss: true),
            [TrashNpc] = new("Trash", "Ferocious Horn Den"),
        });
        engine.Apply(new LocalPlayerEvent(Start, Me, "Local"));
        engine.Apply(new EntitySpawnEvent(Start, Boss, null, BossNpc, Hp: MaxHp, MaxHp: MaxHp));
        engine.Apply(new EntitySpawnEvent(Start, OtherBoss, null, OtherBossNpc, Hp: MaxHp, MaxHp: MaxHp));
        engine.Apply(new EntitySpawnEvent(Start, Helper, null, HelperNpc));
        return engine;
    }

    private static DateTimeOffset At(double seconds) => Start.AddSeconds(seconds);

    private static DamageEvent Hit(double seconds, ulong target, long amount) =>
        new(At(seconds), Me, target, 10, amount, HitModifiers.None);

    /// <summary>Un golpe de <paramref name="source"/> al jugador local.</summary>
    private static DamageEvent Incoming(double seconds, ulong source) =>
        new(At(seconds), source, Me, 500, 50, HitModifiers.None);
}
