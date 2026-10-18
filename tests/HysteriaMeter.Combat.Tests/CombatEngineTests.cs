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
    public void Active_dps_caps_idle_gaps()
    {
        // Golpes en 0, 1 y 7 s: tiempo activo = 1 + 3 (el hueco de 6 s cuenta como 3).
        var engine = CreateEngine();
        engine.Apply(new DamageEvent(At(0), Me, Boss, 1, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(1), Me, Boss, 1, 1000, HitModifiers.None));
        engine.Apply(new DamageEvent(At(7), Me, Boss, 1, 2000, HitModifiers.None));

        var me = Assert.Single(engine.CurrentSnapshot()!.Actors);
        Assert.Equal(1000d, me.ActiveDps);
        Assert.Equal(4000d / 7, me.Dps, 3);
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
}
