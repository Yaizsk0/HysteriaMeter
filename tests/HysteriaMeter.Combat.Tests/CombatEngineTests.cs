using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat.Tests;

public class CombatEngineTests
{
    private const ulong Me = 1;
    private const ulong Ally = 2;
    private const ulong MySummon = 50;
    private const ulong Boss = 900;
    private const ulong Stranger = 7;

    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 21, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private static CombatEngine CreateEngine()
    {
        var engine = new CombatEngine();
        engine.Apply(new LocalPlayerEvent(T0, Me, "Mati"));
        engine.Apply(new EntityInfoEvent(T0, Ally, "Aliado", 3));
        engine.Apply(new PartyMemberSeenEvent(T0, Ally));
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345));
        engine.Apply(new EntityInfoEvent(T0, Boss, "Boss", null));
        return engine;
    }

    [Fact]
    public void Unnamed_monster_takes_its_name_from_the_npc_table()
    {
        var engine = new CombatEngine();
        engine.Apply(new LocalPlayerEvent(T0, Me, "Mati"));
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 10, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(1), Boss, Me, 500, 300, HitModifiers.None));

        var unnamed = engine.CurrentSnapshot()!;
        Assert.Null(unnamed.TargetName);
        Assert.Equal(12345U, unnamed.TargetNpcId);
        Assert.False(unnamed.TargetIsBoss);

        engine.Entities.Npcs = new NpcCatalog(new Dictionary<uint, NpcInfo> { [12345] = new("Destroyer Tarka", "Fortaleza", IsBoss: true, EnrageSeconds: 480) });
        var named = engine.CurrentSnapshot()!;
        Assert.Equal("Destroyer Tarka", named.TargetName);
        Assert.True(named.TargetIsBoss);
        Assert.Equal(480, named.TargetEnrageSeconds);
        Assert.Null(unnamed.TargetEnrageSeconds);
        Assert.Equal("Destroyer Tarka", engine.Entities.DisplayName(Boss));
    }

    [Fact]
    public void Fights_remember_the_zone_entry_they_started_in()
    {
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Me, Boss, 10, 1000, HitModifiers.None));
        Assert.Equal(T0, engine.CurrentSnapshot()!.ZoneEnteredAt);

        // Mismo jugador, misma entidad: sigue siendo la misma zona.
        engine.Apply(new LocalPlayerEvent(At(5), Me, "Mati"));
        Assert.Equal(T0, engine.Entities.ZoneEnteredAt);

        // Cambio de zona: otra entidad para el jugador local.
        engine.Reset();
        engine.Apply(new LocalPlayerEvent(At(60), 999, "Mati"));
        engine.Apply(new DamageEvent(At(61), 999, Boss, 10, 1000, HitModifiers.None));
        Assert.Equal(At(60), engine.CurrentSnapshot()!.ZoneEnteredAt);
    }

    [Fact]
    public void Players_get_class_from_skills_and_gear_from_their_spawn()
    {
        var engine = CreateEngine();
        engine.Apply(new PlayerGearEvent(T0, Ally, 2001, 93_571));
        engine.Apply(new PlayerGearEvent(T0, Me, 2046, null));
        engine.Apply(new LocalCombatPowerEvent(T0, 91_358));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 3000019, 100, HitModifiers.None)); // poción: no dice la clase
        engine.Apply(new DamageEvent(At(1), Me, Boss, 12010350, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Ally, Boss, 15030340, 500, HitModifiers.None));

        var actors = engine.CurrentSnapshot()!.Actors.ToDictionary(a => a.Name);
        Assert.Equal((12, 2046, 91_358L), (actors["Mati"].ClassId, actors["Mati"].ItemLevel, actors["Mati"].CombatPower));
        Assert.Equal((15, 2001, 93_571L), (actors["Aliado"].ClassId, actors["Aliado"].ItemLevel, actors["Aliado"].CombatPower));
        Assert.Null(EntityRegistry.ClassOfSkill(1605143));

        // Cambio de zona: otra entidad para el local, el equipo se mantiene hasta que llegue el nuevo.
        engine.Apply(new LocalPlayerEvent(At(60), 999, "Mati"));
        Assert.Equal((2046, 91_358L), (engine.Entities.Profile(999).ItemLevel, engine.Entities.Profile(999).CombatPower));
    }

    [Fact]
    public void Marks_moments_of_the_current_fight()
    {
        var engine = CreateEngine();
        Assert.Null(engine.Mark(At(0)));

        engine.Apply(new DamageEvent(At(0), Me, Boss, 10, 1000, HitModifiers.None));
        Assert.Equal(42, engine.Mark(At(42.7)));
        Assert.Equal(0, engine.Mark(At(-3)));

        Assert.Equal([42, 0], engine.CurrentSnapshot()!.Marks!);
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
    public void Counts_damage_taken_from_monsters_and_deaths_with_last_hits()
    {
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Me, Boss, 10, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(1), Boss, Me, 500, 300, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Boss, Ally, 501, 200, HitModifiers.Critical));
        engine.Apply(new DamageEvent(At(3), Boss, Stranger, 501, 999, HitModifiers.None)); // fuera de la party
        engine.Apply(new DamageEvent(At(20), Boss, Me, 502, 4000, HitModifiers.Critical));
        engine.Apply(new DeathEvent(At(20.5), Me));

        var snapshot = engine.CurrentSnapshot()!;
        var me = snapshot.Actors.Single(a => a.Id == Me);
        Assert.Equal((4300L, 2, 1), (me.DamageTaken, me.HitsTaken, me.Deaths));
        Assert.Equal(200, snapshot.Actors.Single(a => a.Id == Ally).DamageTaken);
        Assert.DoesNotContain(snapshot.Actors, a => a.Id == Stranger);
        Assert.Equal(1000, snapshot.TotalDamage); // el daño recibido no suma al daño hecho

        var death = Assert.Single(snapshot.Deaths!);
        Assert.Equal("Mati", death.Name);
        Assert.Equal(502U, death.KillingBlow!.SkillId); // el golpe de los 1 s quedó fuera de la ventana de 10 s
        Assert.Single(death.LastHits);
    }

    [Fact]
    public void Damage_taken_is_split_by_the_monster_skill_that_hit()
    {
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Me, Boss, 10, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(1), Boss, Me, 500, 100, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Boss, Me, 500, 200, HitModifiers.Critical));
        engine.Apply(new DamageEvent(At(3), Boss, Me, 501, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(4), Boss, Me, 500, 300, HitModifiers.None));

        foreach (var snapshot in new[] { engine.CurrentSnapshot()!, engine.SessionSnapshot()! })
        {
            var me = snapshot.Actors.Single(a => a.Id == Me);
            Assert.Equal([new TakenSkillSnapshot(501, 1000, 1), new TakenSkillSnapshot(500, 600, 3)], me.DamageTakenBySkill!);
            Assert.Equal(me.DamageTaken, me.DamageTakenBySkill!.Sum(s => s.Damage));
            Assert.Equal(me.HitsTaken, me.DamageTakenBySkill!.Sum(s => s.Hits));
        }
    }

    [Fact]
    public void Without_damage_taken_there_is_no_breakdown()
    {
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Me, Boss, 10, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(1), Ally, Boss, 20, 500, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Boss, Ally, 500, 200, HitModifiers.None));

        var actors = engine.CurrentSnapshot()!.Actors.ToDictionary(a => a.Id);
        Assert.Equal((0L, 0), (actors[Me].DamageTaken, actors[Me].HitsTaken));
        Assert.Null(actors[Me].DamageTakenBySkill);
        Assert.Equal([new TakenSkillSnapshot(500, 200, 1)], actors[Ally].DamageTakenBySkill!);
    }

    [Fact]
    public void Session_adds_every_fight_using_only_combat_time()
    {
        var engine = CreateEngine();
        Assert.Null(engine.SessionSnapshot());

        engine.Apply(new DamageEvent(At(0), Me, Boss, 10, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(5), Me, Boss, 10, 1000, HitModifiers.None));
        engine.Tick(At(30)); // cierra la primera pelea por inactividad
        engine.Apply(new DamageEvent(At(100), Me, Boss, 20, 500, HitModifiers.None));
        engine.Apply(new DamageEvent(At(105), Me, Boss, 10, 500, HitModifiers.None));

        var session = engine.SessionSnapshot()!;
        Assert.Equal("Sesión · 2 peleas", session.TargetName);
        Assert.Equal(TimeSpan.FromSeconds(10), session.Duration); // 5 s + 5 s, sin los 95 s entre peleas
        Assert.Equal(3000, session.Actors.Single(a => a.Id == Me).Damage);
        Assert.Equal(300, session.Actors.Single(a => a.Id == Me).Dps);
        // Los huecos de 4 s dentro de cada pelea y los 94 s entre peleas pasan la tolerancia: quedan los 4 s con golpes,
        // que medidos como el DPS son 3 s. Pelea por pelea serían 1 + 1: el rato entre peleas deja un segundo de más.
        Assert.Equal(3000d / 3, session.Actors.Single(a => a.Id == Me).ActiveDps, 3);
        Assert.Equal(1000, engine.CurrentSnapshot()!.Actors.Single(a => a.Id == Me).Damage);

        engine.Reset();
        Assert.Null(engine.SessionSnapshot());
    }

    [Fact]
    public void Run_adds_the_fights_since_entering_the_zone()
    {
        const ulong MeInNextZone = 3;
        var engine = CreateEngine();
        Assert.Null(engine.RunSnapshot());

        engine.Apply(new DamageEvent(At(0), Me, Boss, 10, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(5), Me, Boss, 10, 1000, HitModifiers.None));
        engine.Tick(At(30));
        engine.Apply(new DamageEvent(At(100), Me, Boss, 20, 500, HitModifiers.None));
        engine.Apply(new DamageEvent(At(105), Me, Boss, 10, 500, HitModifiers.None));

        var run = engine.RunSnapshot()!;
        Assert.Equal("Run 01:45 · 2 peleas", run.TargetName);
        Assert.Equal(TimeSpan.FromSeconds(10), run.Duration);
        Assert.Equal(3000, run.Actors.Single(a => a.Id == Me).Damage);

        // Otra zona (otra conexión, otra entidad): empieza otra run, la sesión sigue sumando.
        engine.Tick(At(150));
        engine.Apply(new LocalPlayerEvent(At(200), MeInNextZone, Name: null));
        engine.Apply(new DamageEvent(At(210), MeInNextZone, Boss, 1, 700, HitModifiers.None));

        Assert.Equal("Run 00:10 · 1 pelea", engine.RunSnapshot()!.TargetName);
        Assert.Equal(700, engine.RunSnapshot()!.Actors.Sum(a => a.Damage));
        Assert.Equal(3700, engine.SessionSnapshot()!.Actors.Sum(a => a.Damage));

        engine.Reset();
        Assert.Null(engine.RunSnapshot());
    }

    [Fact]
    public void Idle_timeout_can_change_while_running()
    {
        var engine = CreateEngine();
        engine.IdleTimeout = TimeSpan.FromSeconds(30);
        engine.Apply(new DamageEvent(At(0), Me, Boss, 10, 1000, HitModifiers.None));
        engine.Tick(At(20));
        Assert.NotNull(engine.CurrentSnapshot());
        engine.Tick(At(31));
        Assert.Null(engine.CurrentSnapshot());
    }

    [Fact]
    public void Keeps_a_log_of_the_fight_with_names()
    {
        var engine = CreateEngine();
        Assert.Empty(engine.CombatLog());

        engine.Apply(new DamageEvent(At(0), Me, Boss, 10, 1000, HitModifiers.Critical));
        engine.Apply(new HealEvent(At(1), Ally, Me, 30, 250));
        engine.Apply(new DamageEvent(At(2), Boss, Ally, 500, 100, HitModifiers.None));

        var log = engine.CombatLog();
        Assert.Equal([LogKind.Damage, LogKind.Heal, LogKind.Damage], log.Select(l => l.Kind));
        Assert.Equal(("Mati", "Boss", HitModifiers.Critical), (log[0].SourceName, log[0].TargetName, log[0].Modifiers));
        Assert.Equal(("Aliado", "Mati", 250L), (log[1].SourceName, log[1].TargetName, log[1].Amount));

        engine.Reset();
        Assert.Equal(3, engine.CombatLog().Count); // queda el de la última pelea
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
    public void Ownerless_summon_using_a_class_skill_goes_to_the_only_player_of_that_class()
    {
        // Kromede: Abyss le da a Rubia (Hechicero) 605k de Bittercold Wind, que pega una invocación sin dueño.
        var engine = CreateEngine();
        engine.Apply(new EntityInfoEvent(T0, Ally, "Aliado", 15));
        engine.Apply(new EntitySpawnEvent(T0, MySummon, OwnerId: null, NpcId: 2920650));
        engine.Apply(new DamageEvent(At(0), MySummon, Boss, 15280143, 2_000, HitModifiers.Critical));
        engine.Apply(new DamageEvent(At(1), MySummon, Boss, 15280143, 3_000, HitModifiers.Critical));

        var actor = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal((Ally, 5_000L), (actor.Id, actor.Damage));
    }

    [Fact]
    public void Ownerless_summon_is_not_guessed_when_two_players_share_the_class()
    {
        var engine = CreateEngine();
        engine.Apply(new EntityInfoEvent(T0, Ally, "Aliado", 15));
        engine.Apply(new EntityInfoEvent(T0, Me, "Mati", 15));
        engine.Apply(new EntitySpawnEvent(T0, MySummon, OwnerId: null, NpcId: 2920650));
        engine.Apply(new DamageEvent(At(0), MySummon, Boss, 15280143, 2_000, HitModifiers.None));

        Assert.Null(engine.CurrentSnapshot());
    }

    [Fact]
    public void Monster_hitting_a_player_is_never_adopted()
    {
        var engine = CreateEngine();
        engine.Apply(new EntityInfoEvent(T0, Ally, "Aliado", 15));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 10, 1_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(1), Boss, Ally, 15280143, 500, HitModifiers.None));

        var actors = engine.CurrentSnapshot()!.Actors;
        Assert.DoesNotContain(actors, a => a.Id == Ally && a.Damage > 0);
        // Con skill de clase sigue siendo daño recibido: a la verificación van solo los golpes a un NPC.
        var ally = actors.Single(a => a.Id == Ally);
        Assert.Equal((500L, 1), (ally.DamageTaken, ally.HitsTaken));
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
        Assert.Equal((132_263L, 58_878L), (skill.DirectDamage, skill.PeriodicDamage));
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
        Assert.Equal(new HitCounts(2, 1, 0, 0, 1, 1, 1), me.Counts);
    }

    [Fact]
    public void Active_dps_leaves_out_gaps_longer_than_the_tolerance()
    {
        // Golpes en 0, 1 y 7 s: la serie es [1000, 1000, 0, 0, 0, 0, 0, 2000]. Del primer golpe al último hay 8 s y los
        // 5 s seguidos sin pegar (del 2 al 6) pasan la tolerancia de 2 s: son tiempo muerto. Quedan 3 s activos, que
        // medidos como el DPS (del 0 al 7 son 7 s) dan 7 − 5 = 2 s.
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(1), Me, Boss, 1, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(7), Me, Boss, 1, 2000, HitModifiers.None));

        var me = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal((8, 5), (FightActivity.From(me.Timeline).Seconds, FightActivity.From(me.Timeline).DeadSeconds));
        Assert.Equal(4000d / 2, me.ActiveDps, 3);
        Assert.Equal(4000d / 7, me.Dps, 3);
    }

    [Fact]
    public void Without_dead_time_the_active_dps_is_the_dps()
    {
        // Golpes en 0, 1, 2 y 3 s: ACTIVO 100 % y TIEMPO MUERTO 0 s, así que el DPS activo no puede quedar por debajo del
        // DPS. Contando los 4 segundos con golpes daba 4000 / 4; medido como el DPS, del 0 al 3 son 3 s.
        var engine = CreateEngine();
        for (var second = 0; second < 4; second++)
        {
            engine.Apply(new DamageEvent(At(second), Me, Boss, 1, 1000, HitModifiers.None));
        }

        var me = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal(0, FightActivity.Of(me).DeadSeconds);
        Assert.Equal(4000d / 3, me.Dps, 3);
        Assert.Equal(me.Dps, me.ActiveDps, 3);
    }

    [Fact]
    public void Active_dps_of_a_player_dead_20_s_in_a_2_minute_fight_uses_100_s()
    {
        // Escenario de spec.md (002, historia del DPS activo): el aliado pega del 0:00 al 2:00, así la pelea dura 2 min y
        // no se corta; vos pegás 1.000 por segundo salvo los 20 s que estuviste muerto (del 50 al 69: TIEMPO MUERTO 20 s).
        // Alcance "todos": el aliado no manda HP/MP y a los 30 s dejaría de contar como party.
        var engine = CreateEngine();
        engine.Scope = ActorScope.Everyone;
        for (var second = 0; second <= 120; second++)
        {
            engine.Apply(new DamageEvent(At(second), Ally, Boss, 2, 500, HitModifiers.None));
            if (second is < 50 or >= 70)
            {
                engine.Apply(new DamageEvent(At(second), Me, Boss, 1, 1000, HitModifiers.None));
            }
        }

        var snapshot = engine.CurrentSnapshot()!;
        var me = snapshot.Actors.Single(a => a.Id == Me);
        Assert.Equal(TimeSpan.FromMinutes(2), snapshot.Duration);
        Assert.Equal(20, FightActivity.Of(me).DeadSeconds);
        Assert.Equal(101_000, me.Damage); // 50 golpes antes de morir y 51 después (del 70 al 120)
        Assert.Equal(101_000d / 100, me.ActiveDps, 3);
    }

    [Fact]
    public void Dot_ticks_that_fill_a_gap_do_not_change_the_active_dps()
    {
        // Golpes directos del 0 al 9 y del 30 al 39; en el medio solo pega el DoT, con el jugador muerto o quieto. El daño
        // del DoT es suyo y suma al daño, pero esos 20 s no son el jugador actuando: ni su tiempo ni sus ticks cuentan para
        // el DPS activo, que da lo mismo que en la pelea sin esos ticks. Activos, 20 s, que medidos como el DPS son
        // 39 − 20 = 19 s (antes, con los ticks como actividad, contaban 39). El aliado pega todo el tiempo para que, sin
        // los ticks, la pelea no se corte en el hueco; alcance "todos" porque no manda HP/MP.
        var withDot = CreateEngine();
        var withoutDot = CreateEngine();
        withDot.Scope = withoutDot.Scope = ActorScope.Everyone;
        for (var second = 0; second < 40; second++)
        {
            withDot.Apply(new DamageEvent(At(second), Ally, Boss, 2, 500, HitModifiers.None));
            withoutDot.Apply(new DamageEvent(At(second), Ally, Boss, 2, 500, HitModifiers.None));
            if (second is >= 10 and < 30)
            {
                withDot.Apply(new DamageEvent(At(second), Me, Boss, 7, 500, HitModifiers.DamageOverTime));
                continue;
            }

            withDot.Apply(new DamageEvent(At(second), Me, Boss, 1, 1000, HitModifiers.None));
            withoutDot.Apply(new DamageEvent(At(second), Me, Boss, 1, 1000, HitModifiers.None));
        }

        var me = withDot.CurrentSnapshot()!.Actors.Single(a => a.Id == Me);
        Assert.Equal(30_000, me.Damage);
        Assert.Equal(20_000d / 19, me.ActiveDps, 3);
        Assert.Equal(withoutDot.CurrentSnapshot()!.Actors.Single(a => a.Id == Me).ActiveDps, me.ActiveDps, 3);
    }

    [Fact]
    public void A_dot_tail_after_the_last_direct_hit_does_not_inflate_the_active_dps()
    {
        // Un golpe directo de 1.000 en el 0 y después solo DoT, 300 por segundo del 1 al 9 (murió o dejó de pegar). Con todo
        // el daño sobre su único segundo activo daba 3.700, 9 veces el DPS; los ticks de después no son el jugador actuando.
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 1000, HitModifiers.None));
        for (var second = 1; second < 10; second++)
        {
            engine.Apply(new DamageEvent(At(second), Me, Boss, 7, 300, HitModifiers.DamageOverTime));
        }

        var me = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal(3_700, me.Damage);
        Assert.Equal(3_700d / 9, me.Dps, 3);
        Assert.Equal(1_000d, me.ActiveDps, 3); // el daño de su segundo activo sobre 1 s, el mínimo, como el DPS
    }

    [Fact]
    public void A_player_that_only_did_dot_has_no_active_dps()
    {
        // Sin golpes directos no hay tiempo activo que medir: 0 (la UI muestra "–"), como el ACTIVO del detalle.
        var engine = CreateEngine();
        for (var second = 0; second < 10; second++)
        {
            engine.Apply(new DamageEvent(At(second), Ally, Boss, 2, 1000, HitModifiers.None));
            engine.Apply(new DamageEvent(At(second), Me, Boss, 7, 300, HitModifiers.DamageOverTime));
        }

        var me = engine.CurrentSnapshot()!.Actors.Single(a => a.Id == Me);
        Assert.Equal(3_000, me.Damage);
        Assert.Equal(0d, me.ActiveDps);
    }

    [Fact]
    public void Session_and_run_active_dps_count_only_combat_time()
    {
        // Dos peleas pegando todos los segundos (del 0 al 9 y del 100 al 109, 9 s cada una como el DPS) y 90 s sin pelea
        // entre ellas: sin tiempo muerto, el DPS activo es el DPS en cada pelea, en la sesión y en la run. Los 90 s entre
        // peleas son un hueco muerto de la serie de la sesión; el segundo "de más" que deja se topea con los 18 s de combate.
        var engine = CreateEngine();
        for (var second = 0; second < 10; second++)
        {
            engine.Apply(new DamageEvent(At(second), Me, Boss, 1, 1000, HitModifiers.None));
        }

        engine.Tick(At(30));
        for (var second = 100; second < 110; second++)
        {
            engine.Apply(new DamageEvent(At(second), Me, Boss, 1, 500, HitModifiers.None));
        }

        foreach (var snapshot in new[] { engine.SessionSnapshot()!, engine.RunSnapshot()! })
        {
            var me = snapshot.Actors.Single(a => a.Id == Me);
            Assert.Equal(15_000d / 18, me.Dps, 3);
            Assert.Equal(me.Dps, me.ActiveDps, 3);
        }

        Assert.Equal(5_000d / 9, engine.CurrentSnapshot()!.Actors.Single(a => a.Id == Me).ActiveDps, 3);
    }

    [Fact]
    public void Timeline_keeps_damage_per_second_since_the_start()
    {
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(0.5), Me, Boss, 1, 500, HitModifiers.None));
        engine.Apply(new DamageEvent(At(3), Me, Boss, 1, 2000, HitModifiers.None));

        var me = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal([1500L, 0, 0, 2000], me.Timeline);
        // Sin DoT la serie directa sería igual: no se arma y se usa el Timeline.
        Assert.Null(me.DirectTimeline);
    }

    [Fact]
    public void Direct_timeline_leaves_out_dot_ticks()
    {
        // Un DoT sigue pegando con el jugador muerto: el Timeline lo suma (es daño de verdad) y la serie directa no, así la
        // vuelta después de una muerte es el siguiente golpe directo. La serie directa termina en el último golpe directo.
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Me, Boss, 7, 500, HitModifiers.DamageOverTime));
        engine.Apply(new DamageEvent(At(3), Me, Boss, 1, 2000, HitModifiers.Critical));
        engine.Apply(new DamageEvent(At(4), Me, Boss, 7, 500, HitModifiers.DamageOverTime));

        var me = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal([1000L, 0, 500, 2000, 500], me.Timeline);
        Assert.Equal([1000L, 0, 0, 2000], me.DirectTimeline);
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
    public void Hits_after_the_boss_dies_do_not_open_a_phantom_encounter()
    {
        const ulong add = 901;
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;

        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 100, HitModifiers.None));
        engine.Apply(new DeathEvent(At(3), Boss));

        // Golpe en vuelo al cadáver, costo de vida propio y "daño" a un compañero: nada de esto es una pelea.
        engine.Apply(new DamageEvent(At(3.1), Ally, Boss, 1, 500, HitModifiers.None));
        engine.Apply(new DamageEvent(At(3.2), Ally, Ally, 1, 884, HitModifiers.None));
        engine.Apply(new DamageEvent(At(3.3), Me, Ally, 1, 1076, HitModifiers.None));
        Assert.Null(engine.CurrentSnapshot());

        // El próximo monstruo sí abre una pelea nueva.
        engine.Apply(new EntitySpawnEvent(At(4), add, null, 2001));
        engine.Apply(new DamageEvent(At(5), Me, add, 1, 100, HitModifiers.None));
        Assert.Equal(100, engine.CurrentSnapshot()!.TotalDamage);
        Assert.Single(finished);
    }

    [Fact]
    public void Healing_is_tracked_during_encounter()
    {
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 100, HitModifiers.None));
        engine.Apply(new HealEvent(At(1), Ally, Me, 7, 300));

        Assert.Equal(300, engine.CurrentSnapshot()!.Actors.Single(a => a.Id == Ally).Healing);
    }

    [Fact]
    public void Local_player_keeps_name_after_zone_change()
    {
        const ulong MeInNewZone = 3;
        var engine = CreateEngine();
        engine.Apply(new LocalPlayerEvent(At(0), MeInNewZone, Name: null));
        engine.Apply(new DamageEvent(At(1), MeInNewZone, Boss, 1, 100, HitModifiers.None));

        var me = engine.CurrentSnapshot()!.Actors.Single();
        Assert.Equal((MeInNewZone, "Mati", true), (me.Id, me.Name, me.IsLocalPlayer));
    }

    [Fact]
    public void Local_player_name_follows_character_change()
    {
        const ulong OtherCharacter = 4;
        var engine = CreateEngine();
        engine.Apply(new LocalPlayerEvent(At(0), OtherCharacter, Name: null));
        engine.Apply(new LocalPlayerEvent(At(6), OtherCharacter, "Alt"));
        engine.Apply(new DamageEvent(At(7), OtherCharacter, Boss, 1, 100, HitModifiers.None));

        var me = engine.CurrentSnapshot()!.Actors.Single();
        Assert.Equal(("Alt", true), (me.Name, me.IsLocalPlayer));
    }

    [Fact]
    public void Party_scope_ignores_players_outside_the_party()
    {
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Stranger, Boss, 1, 5000, HitModifiers.None));
        Assert.Null(engine.CurrentSnapshot()); // un random no abre encuentro

        engine.Apply(new DamageEvent(At(1), Me, Boss, 1, 100, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Ally, Boss, 1, 50, HitModifiers.None));
        engine.Apply(new DamageEvent(At(3), Stranger, Boss, 1, 5000, HitModifiers.None));

        Assert.Equal([Me, Ally], engine.CurrentSnapshot()!.Actors.Select(a => a.Id));
    }

    [Theory]
    [InlineData(ActorScope.Me, 1)]
    [InlineData(ActorScope.Party, 2)]
    [InlineData(ActorScope.Everyone, 3)]
    public void Scope_decides_who_is_counted(ActorScope scope, int expectedActors)
    {
        var engine = CreateEngine();
        engine.Scope = scope;
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 100, HitModifiers.None));
        engine.Apply(new DamageEvent(At(1), Ally, Boss, 1, 100, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Stranger, Boss, 1, 100, HitModifiers.None));

        Assert.Equal(expectedActors, engine.CurrentSnapshot()!.Actors.Count);
    }

    [Fact]
    public void Without_split_damage_accumulates_across_idle_gaps()
    {
        var engine = CreateEngine();
        engine.SplitEncounters = false;
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 100, HitModifiers.None));
        engine.Tick(At(60));
        engine.Apply(new DamageEvent(At(60), Me, Boss, 1, 100, HitModifiers.None));

        Assert.Equal(200, engine.CurrentSnapshot()!.TotalDamage);
    }

    [Fact]
    public void Member_that_stops_reporting_leaves_the_party()
    {
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(30), Me, Boss, 1, 100, HitModifiers.None));
        engine.Apply(new DamageEvent(At(31), Ally, Boss, 1, 100, HitModifiers.None)); // sin HP/MP hace 31 s

        Assert.Equal([Me], engine.CurrentSnapshot()!.Actors.Select(a => a.Id));
    }

    [Fact]
    public void Tracks_target_health_and_overkill_of_the_last_hit()
    {
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345, Hp: 1000, MaxHp: 1000));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 400, HitModifiers.None));
        engine.Apply(new HealthEvent(At(1), Boss, 600));
        engine.Apply(new DamageEvent(At(2), Ally, Boss, 1, 450, HitModifiers.None));

        var snapshot = engine.CurrentSnapshot()!;
        Assert.Equal(new Health(150, 1000), snapshot.TargetHealth);

        engine.Apply(new DamageEvent(At(3), Me, Boss, 1, 500, HitModifiers.None));
        var me = engine.CurrentSnapshot()!.Actors.Single(a => a.Id == Me);
        Assert.Equal((900L, 350L), (me.Damage, me.Overkill));
    }

    [Fact]
    public void Records_when_the_boss_crosses_each_phase()
    {
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345, Hp: 1000, MaxHp: 1000));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 200, HitModifiers.None));
        engine.Apply(new DamageEvent(At(3), Me, Boss, 1, 100, HitModifiers.None));
        engine.Apply(new HealthEvent(At(5), Boss, 400));
        engine.Apply(new DamageEvent(At(7), Me, Boss, 1, 300, HitModifiers.None));

        // 800 (80 %) → 700 cruza el 75 a los 3 s; 400 cruza el 50 a los 5 s; 100 cruza el 25 a los 7 s.
        Assert.Equal([new HealthPhase(75, 3), new HealthPhase(50, 5), new HealthPhase(25, 7)], engine.CurrentSnapshot()!.Phases);
    }

    [Fact]
    public void Phases_already_passed_when_the_capture_started_are_not_invented()
    {
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345, Hp: 400, MaxHp: 1000));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 100, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Me, Boss, 1, 100, HitModifiers.None));

        Assert.Equal([new HealthPhase(25, 2)], engine.CurrentSnapshot()!.Phases);
    }

    [Fact]
    public void Server_health_is_kept_apart_from_the_damage_we_count()
    {
        // HealthOf descuenta nuestros golpes (overkill, fases, tooltip); la vida del servidor es la que informó y nada más.
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345, Hp: 1000, MaxHp: 1000));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 400, HitModifiers.None));
        Assert.Equal((new Health(600, 1000), new Health(1000, 1000)), (engine.Entities.HealthOf(Boss), engine.Entities.ServerHealthOf(Boss)));

        engine.Apply(new HealthEvent(At(1), Boss, 500));
        engine.Apply(new DamageEvent(At(2), Me, Boss, 1, 100, HitModifiers.None));
        Assert.Equal((new Health(400, 1000), new Health(500, 1000)), (engine.Entities.HealthOf(Boss), engine.Entities.ServerHealthOf(Boss)));
        Assert.Null(engine.Entities.ServerHealthOf(Ally));
    }

    [Fact]
    public void Verification_finds_the_damage_missing_from_what_the_server_reports()
    {
        // 10.000 de vida y dos golpes de 3.000, pero el servidor informa 2.000: faltan 2.000 de daño (un golpe sin decodificar).
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345, Hp: 10_000, MaxHp: 10_000));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 3_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(1), Ally, Boss, 1, 3_000, HitModifiers.None));
        engine.Apply(new HealthEvent(At(1), Boss, 2_000));

        Assert.Equal(
            new TargetVerification(Boss, 12345, StartHp: 10_000, EndHp: 2_000, MaxHp: 10_000, Counted: 6_000, Overkill: 0, Difference: -2_000, Closes: false),
            Assert.Single(engine.CurrentSnapshot()!.Verifications!));
        // Solo la pelea: la sesión y la run suman peleas distintas.
        Assert.Null(engine.SessionSnapshot()!.Verifications);
        Assert.Null(engine.RunSnapshot()!.Verifications);
    }

    [Fact]
    public void Verification_closes_when_every_hit_is_counted_up_to_the_kill()
    {
        // 3.000 + 3.000 + 2.000 y el servidor informa 2.000: cierra. El golpe final de 2.500 lo mata con 500 de overkill y
        // sigue cerrando: 10.500 contados − 500 de overkill = los 10.000 que perdió.
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345, Hp: 10_000, MaxHp: 10_000));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 3_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(1), Ally, Boss, 1, 3_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Me, Boss, 1, 2_000, HitModifiers.None));
        engine.Apply(new HealthEvent(At(2), Boss, 2_000));

        Assert.Equal(
            new TargetVerification(Boss, 12345, 10_000, 2_000, 10_000, Counted: 8_000, Overkill: 0, Difference: 0, Closes: true),
            Assert.Single(engine.CurrentSnapshot()!.Verifications!));

        engine.Apply(new DamageEvent(At(3), Me, Boss, 1, 2_500, HitModifiers.None));
        engine.Apply(new DeathEvent(At(3), Boss));

        Assert.Equal(
            new TargetVerification(Boss, 12345, 10_000, EndHp: 0, 10_000, Counted: 10_500, Overkill: 500, Difference: 0, Closes: true),
            Assert.Single(Assert.Single(finished).Verifications!));
    }

    [Fact]
    public void Verification_shows_a_final_hit_that_could_not_have_killed()
    {
        // Quedaban 2.000 y el golpe final fue de 1.500: con la muerte faltan 500 de daño (10 veces la tolerancia de 100).
        // El servidor manda igual su 0x8D00 de 0, el del golpe mortal que no decodificamos.
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345, Hp: 10_000, MaxHp: 10_000));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 3_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(1), Ally, Boss, 1, 3_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Me, Boss, 1, 2_000, HitModifiers.None));
        engine.Apply(new HealthEvent(At(2), Boss, 2_000));
        engine.Apply(new DamageEvent(At(3), Me, Boss, 1, 1_500, HitModifiers.None));
        engine.Apply(new HealthEvent(At(3), Boss, 0));
        engine.Apply(new DeathEvent(At(3), Boss));

        Assert.Equal(
            new TargetVerification(Boss, 12345, 10_000, 0, 10_000, Counted: 9_500, Overkill: 0, Difference: -500, Closes: false),
            Assert.Single(Assert.Single(finished).Verifications!));
    }

    [Fact]
    public void Verification_takes_the_overkill_from_the_last_positive_reading()
    {
        // Así llega una muerte en las capturas: el 0x8D00 de 0 después del golpe mortal, golpes que ya estaban en vuelo y
        // recién ahí el 0x8D04. Quedaban 2.000: los 500 que pasó el golpe de 2.500 y los 300 de después son overkill.
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345, Hp: 10_000, MaxHp: 10_000));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 3_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(1), Ally, Boss, 1, 3_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Me, Boss, 1, 2_000, HitModifiers.None));
        engine.Apply(new HealthEvent(At(2), Boss, 2_000));
        engine.Apply(new DamageEvent(At(3), Me, Boss, 1, 2_500, HitModifiers.None));
        engine.Apply(new HealthEvent(At(3), Boss, 0));
        engine.Apply(new DamageEvent(At(3), Ally, Boss, 1, 300, HitModifiers.None));
        engine.Apply(new DeathEvent(At(3), Boss));

        Assert.Equal(
            new TargetVerification(Boss, 12345, 10_000, 0, 10_000, Counted: 10_800, Overkill: 800, Difference: 0, Closes: true),
            Assert.Single(Assert.Single(finished).Verifications!));
    }

    [Fact]
    public void Verification_of_a_respawn_without_health_starts_at_its_first_reading()
    {
        // El mismo id vuelve a aparecer con un spawn sin vida: la que informó el servidor antes (el 0 de su muerte) ya no
        // es la suya, así que la cuenta de la pelea nueva arranca en su primera lectura.
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345, Hp: 10_000, MaxHp: 10_000));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 10_000, HitModifiers.None));
        engine.Apply(new HealthEvent(At(0), Boss, 0));
        engine.Apply(new DeathEvent(At(0), Boss));

        engine.Apply(new EntitySpawnEvent(At(60), Boss, null, NpcId: 12345));
        engine.Apply(new DamageEvent(At(61), Me, Boss, 1, 2_000, HitModifiers.None));
        engine.Apply(new HealthEvent(At(61), Boss, 8_000));
        engine.Apply(new DamageEvent(At(62), Ally, Boss, 1, 3_000, HitModifiers.None));
        engine.Apply(new HealthEvent(At(62), Boss, 5_000));

        Assert.Equal(
            new TargetVerification(Boss, 12345, StartHp: 8_000, EndHp: 5_000, MaxHp: 10_000, Counted: 3_000, Overkill: 0, Difference: 0, Closes: true),
            Assert.Single(engine.CurrentSnapshot()!.Verifications ?? []));
    }

    [Fact]
    public void Verification_counts_players_outside_the_filter()
    {
        // Con el filtro de party, el golpe de un jugador de afuera no entra al ranking pero le baja la vida al boss. El
        // que da antes de que haya pelea no abre ninguna ni se cuenta: la vida inicial ya lo refleja.
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345, Hp: 10_000, MaxHp: 10_000));
        engine.Apply(new DamageEvent(At(0), Stranger, Boss, 1, 1_000, HitModifiers.None));
        engine.Apply(new HealthEvent(At(0), Boss, 9_000));
        engine.Apply(new DamageEvent(At(1), Me, Boss, 1, 3_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), Stranger, Boss, 1, 3_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(3), Ally, Boss, 1, 2_000, HitModifiers.None));
        engine.Apply(new HealthEvent(At(3), Boss, 1_000));

        var snapshot = engine.CurrentSnapshot()!;
        Assert.Equal([Me, Ally], snapshot.Actors.Select(a => a.Id));
        Assert.Equal(
            new TargetVerification(Boss, 12345, StartHp: 9_000, EndHp: 1_000, 10_000, Counted: 8_000, Overkill: 0, Difference: 0, Closes: true),
            Assert.Single(snapshot.Verifications!));
    }

    [Fact]
    public void Verification_counts_ownerless_summons_when_the_owner_is_ambiguous()
    {
        // Un intervalo real de la comparación del 2026-10-17: la vida del boss baja de 7.549.353 a 7.528.827 (20.526) con
        // 11.429 de jugadores y 9.097 de una invocación sin dueño (skill 15200023). Con dos jugadores de clase 15 no se
        // adopta ni entra al ranking, pero la verificación la suma y cierra.
        var engine = CreateEngine();
        engine.Apply(new EntityInfoEvent(T0, Ally, "Aliado", 15));
        engine.Apply(new EntityInfoEvent(T0, Me, "Mati", 15));
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345, Hp: 7_549_353, MaxHp: 8_208_000));
        engine.Apply(new EntitySpawnEvent(T0, MySummon, OwnerId: null, NpcId: 2920735));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 1_360, HitModifiers.None));
        engine.Apply(new DamageEvent(At(0), MySummon, Boss, 15200023, 9_097, HitModifiers.None));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 6_408, HitModifiers.None));
        engine.Apply(new DamageEvent(At(0), Ally, Boss, 1, 3_661, HitModifiers.None));
        engine.Apply(new HealthEvent(At(0), Boss, 7_528_827));

        var snapshot = engine.CurrentSnapshot()!;
        Assert.Equal(
            new TargetVerification(Boss, 12345, StartHp: 7_549_353, EndHp: 7_528_827, MaxHp: 8_208_000, Counted: 20_526, Overkill: 0, Difference: 0, Closes: true),
            Assert.Single(snapshot.Verifications!));
        Assert.Equal([(Me, 7_768L), (Ally, 3_661L)], snapshot.Actors.Select(a => (a.Id, a.Damage)));
        Assert.Equal(11_429, snapshot.TotalDamage);
    }

    [Fact]
    public void Verification_leaves_out_monster_damage_that_is_not_a_player_skill()
    {
        // Golpes de NPC a NPC que no son daño de jugadores: entre entidades del npc 2920091 (skill 1600750), la curación
        // del boss que se lee como daño (1910071) y la invocación que se pega a sí misma al aparecer (16990004).
        const ulong mechanic = 901;
        const ulong otherMechanic = 902;
        const ulong add = 903;
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, Boss, null, NpcId: 12345, Hp: 10_000, MaxHp: 10_000));
        engine.Apply(new EntitySpawnEvent(T0, mechanic, null, NpcId: 2920091, Hp: 8_208_000, MaxHp: 8_208_000));
        engine.Apply(new EntitySpawnEvent(T0, otherMechanic, null, NpcId: 2920091, Hp: 8_208_000, MaxHp: 8_208_000));
        engine.Apply(new EntitySpawnEvent(T0, add, null, NpcId: 777));
        engine.Apply(new EntitySpawnEvent(T0, MySummon, OwnerId: null, NpcId: 2920113, Hp: 150_000, MaxHp: 150_000));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 3_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(1), mechanic, otherMechanic, 1600750, 1_768, HitModifiers.None));
        engine.Apply(new DamageEvent(At(2), add, Boss, 1910071, 31_500, HitModifiers.None));
        engine.Apply(new DamageEvent(At(3), MySummon, MySummon, 16990004, 100_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(4), Ally, Boss, 1, 2_000, HitModifiers.None));
        engine.Apply(new HealthEvent(At(4), Boss, 5_000));
        engine.Apply(new HealthEvent(At(4), otherMechanic, 8_206_232));
        engine.Apply(new HealthEvent(At(4), MySummon, 50_000));

        Assert.Equal(
            new TargetVerification(Boss, 12345, StartHp: 10_000, EndHp: 5_000, MaxHp: 10_000, Counted: 5_000, Overkill: 0, Difference: 0, Closes: true),
            Assert.Single(engine.CurrentSnapshot()!.Verifications!));
    }

    [Fact]
    public void Ownerless_summon_hits_neither_open_nor_extend_a_fight()
    {
        // Sin dueño, la invocación no es un jugador del ranking: sus golpes no sostienen la pelea ni abren otra.
        var engine = CreateEngine();
        var finished = new List<EncounterSnapshot>();
        engine.EncounterFinished += finished.Add;
        engine.Apply(new EntityInfoEvent(T0, Ally, "Aliado", 15));
        engine.Apply(new EntityInfoEvent(T0, Me, "Mati", 15));
        engine.Apply(new EntitySpawnEvent(T0, MySummon, OwnerId: null, NpcId: 2920655));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 1_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(5), MySummon, Boss, 15280142, 2_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(7.5), MySummon, Boss, 15280142, 2_000, HitModifiers.None));
        engine.Tick(At(8.5));

        var fight = Assert.Single(finished);
        Assert.Equal(At(0), fight.End);
        Assert.Equal([Me], fight.Actors.Select(a => a.Id));

        engine.Apply(new DamageEvent(At(9), MySummon, Boss, 15280142, 2_000, HitModifiers.None));
        Assert.Null(engine.CurrentSnapshot());
    }

    [Fact]
    public void Verification_starts_at_the_first_reading_when_the_spawn_had_no_health()
    {
        // Sin la vida del spawn (no la trajo, o se empezó a capturar después de la aparición), el primer 0x8D00 llega después
        // del primer golpe, que ya refleja (como el del boss de la dungeon de la 001). La cuenta arranca en esa lectura; el
        // golpe de antes no se cuenta.
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 2_082, HitModifiers.None));
        engine.Apply(new HealthEvent(At(0), Boss, 7_918));
        engine.Apply(new DamageEvent(At(1), Ally, Boss, 1, 3_000, HitModifiers.None));
        engine.Apply(new HealthEvent(At(1), Boss, 4_918));

        Assert.Equal(
            new TargetVerification(Boss, 12345, StartHp: 7_918, EndHp: 4_918, MaxHp: 7_918, Counted: 3_000, Overkill: 0, Difference: 0, Closes: true),
            Assert.Single(engine.CurrentSnapshot()!.Verifications!));
    }

    [Fact]
    public void Targets_without_server_health_or_a_later_reading_are_not_verified()
    {
        // Sin vida del servidor no hay con qué comparar; con vida pero sin ninguna lectura después del primer golpe ni
        // muerte, tampoco: no se inventa un "cierra".
        const ulong add = 901;
        var engine = CreateEngine();
        engine.Apply(new EntitySpawnEvent(T0, add, null, NpcId: 777, Hp: 5_000, MaxHp: 5_000));
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 3_000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(1), Me, add, 1, 1_000, HitModifiers.None));

        Assert.Null(engine.CurrentSnapshot()!.Verifications);

        // La única lectura del boss es la primera de la pelea: de ahí arranca la cuenta, todavía sin nada que verificar.
        engine.Apply(new HealthEvent(At(2), Boss, 4_000));
        Assert.Null(engine.CurrentSnapshot()!.Verifications);
    }
}
