using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat;

public sealed record CombatEngineOptions
{
    /// <summary>
    /// Sin daño hecho, ni recibido de un NPC que no sea el objetivo principal, durante este tiempo, el encuentro se cierra
    /// (valor inicial de <see cref="CombatEngine.IdleTimeout"/>). Una pelea de boss espera hasta
    /// <see cref="BossPauseTimeout"/>.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Inactividad que cierra una pelea cuyo objetivo principal es un boss del catálogo de NPCs vivo y sin resetear, si es
    /// más larga que <see cref="IdleTimeout"/>: las pausas de fase, sin daño hecho, no la cortan si después se le vuelve a
    /// pegar a algo que ya es parte de la pelea.
    /// </summary>
    public TimeSpan BossPauseTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Cerrar el encuentro cuando muere el objetivo principal.</summary>
    public bool EndOnMainTargetDeath { get; init; } = true;
}

/// <summary>De quién se cuenta el daño y la curación.</summary>
public enum ActorScope
{
    /// <summary>Todos los jugadores cercanos, incluidos los que no son de la party.</summary>
    Everyone,

    /// <summary>El jugador local y su party.</summary>
    Party,

    /// <summary>Solo el jugador local (y sus invocaciones).</summary>
    Me,
}

/// <summary>
/// Modelo de combate puro (sin I/O): consume eventos y mantiene el encuentro actual.
/// <see cref="Apply"/> y <see cref="Tick"/> se llaman desde un único hilo (el pipeline);
/// <see cref="CurrentSnapshot"/> se puede leer desde cualquiera (la UI).
/// </summary>
public sealed class CombatEngine(CombatEngineOptions options)
{
    /// <summary>
    /// Un monto de <c>0x3804</c> o <c>0x3805</c> por encima de esto no es daño y se ignora entero. En dos peleas de boss
    /// del 2026-10-15, la skill 1000007 (no es de clase) pegó 14 veces exactamente 500.000.000, cada vez a una entidad
    /// distinta que nadie más golpeó. INTERPRETADO: es una mecánica del juego, no un error del decoder; sin captura de
    /// esas peleas no se sabe cuál. El golpe real más grande de la base es de 399.000 y la vida más grande verificada,
    /// de 15.504.000 (ver protocol-notes, "Golpes de 500.000.000").
    /// </summary>
    public const long MaxRealHit = 100_000_000;

    private readonly Lock _gate = new();
    private Encounter? _current;
    private IReadOnlyList<CombatLogEntry> _lastLog = [];
    private TimeSpan _idleTimeout = options.IdleTimeout;

    // Sesión: suma todas las peleas desde el último reinicio (como el "Overall" de Details!).
    private Encounter? _session;
    private TimeSpan _sessionCombat;
    private int _sessionFights;

    // La "run": todas las peleas desde que entramos a la zona actual (dungeon), trash y bosses juntos.
    private Encounter? _run;
    private TimeSpan _runCombat;
    private int _runFights;
    private DateTimeOffset? _runZone;
    private ActorScope _scope = ActorScope.Party;
    private bool _splitEncounters = true;

    public CombatEngine() : this(new CombatEngineOptions())
    {
    }

    public EntityRegistry Entities { get; } = new();

    /// <summary>
    /// Sin daño hecho, ni recibido de un NPC que no sea el objetivo principal, durante este tiempo, el encuentro se cierra.
    /// Una pelea de boss espera hasta <see cref="CombatEngineOptions.BossPauseTimeout"/>. Se puede cambiar en caliente.
    /// </summary>
    public TimeSpan IdleTimeout
    {
        get { lock (_gate) { return _idleTimeout; } }
        set { lock (_gate) { _idleTimeout = value; } }
    }

    /// <summary>
    /// De quién se cuenta el daño. Mientras no se conoce al jugador local se cuenta a todos,
    /// para no dejar el overlay vacío los primeros segundos de una conexión.
    /// </summary>
    public ActorScope Scope
    {
        get { lock (_gate) { return _scope; } }
        set { lock (_gate) { _scope = value; } }
    }

    /// <summary>
    /// Con <c>true</c> cada pelea es un encuentro nuevo (se corta por inactividad o por la muerte del
    /// objetivo). Con <c>false</c> todo se acumula hasta el reinicio manual.
    /// </summary>
    public bool SplitEncounters
    {
        get { lock (_gate) { return _splitEncounters; } }
        set { lock (_gate) { _splitEncounters = value; } }
    }

    /// <summary>Se dispara cuando un encuentro termina (idle o muerte del objetivo).</summary>
    public event Action<EncounterSnapshot>? EncounterFinished;

    public void Apply(CombatEvent e)
    {
        lock (_gate)
        {
            Entities.Apply(e);
            switch (e)
            {
                case DamageEvent damage:
                    OnDamage(damage);
                    break;
                case HealEvent heal:
                    OnHeal(heal);
                    break;
                case DeathEvent death:
                    OnDeath(death);
                    break;
                case HealthEvent health:
                    TrackPhases(health.EntityId, null, health.Timestamp);
                    TrackReset(health.EntityId);
                    if (VerifyingFight(health.Timestamp, health.EntityId) is { } fight && fight.Verifications.TryGetValue(health.EntityId, out var count))
                    {
                        count.Reading(health.Hp);
                    }

                    break;
                case EntitySpawnEvent spawn:
                    if (spawn.Hp is not null)
                    {
                        TrackReset(spawn.EntityId);
                    }

                    TrackRespawn(spawn);
                    break;
            }
        }
    }

    /// <summary>Avanza el reloj: cierra el encuentro si pasó el tiempo de inactividad.</summary>
    public void Tick(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (IsIdle(now))
            {
                Finish();
            }
        }
    }

    /// <summary>Cierra a mano el encuentro actual (botón "reset") y empieza una sesión nueva.</summary>
    /// <summary>
    /// Marca un momento de la pelea en curso (por ejemplo, "acá tiré el burst"). Devuelve los segundos desde el
    /// inicio, o <c>null</c> si no hay pelea.
    /// </summary>
    public int? Mark(DateTimeOffset at)
    {
        lock (_gate)
        {
            if (_current is null)
            {
                return null;
            }

            var second = Math.Max(0, (int)(at - _current.Start).TotalSeconds);
            _current.Marks.Add(second);
            return second;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            Finish();
            _session = null;
            _sessionCombat = TimeSpan.Zero;
            _sessionFights = 0;
            _run = null;
            _runCombat = TimeSpan.Zero;
            _runFights = 0;
        }
    }

    /// <summary>
    /// Todas las peleas desde el último reinicio, como una sola. La duración es la suma del tiempo en combate
    /// (sin los ratos entre peleas), así el DPS es comparable con el de cada pelea.
    /// </summary>
    public EncounterSnapshot? SessionSnapshot()
    {
        lock (_gate)
        {
            if (_session is null)
            {
                return null;
            }

            var combat = _sessionCombat + (_current is null ? TimeSpan.Zero : _current.LastActivity - _current.Start);
            return BuildSnapshot(_session, combat) with { TargetName = SessionTitle(_sessionFights) };
        }
    }

    /// <summary>
    /// Todas las peleas desde que entramos a la zona actual (trash y bosses de la dungeon juntos). El DPS va sobre el
    /// tiempo en combate; el título lleva el tiempo desde la entrada.
    /// </summary>
    public EncounterSnapshot? RunSnapshot()
    {
        lock (_gate)
        {
            if (_run is null)
            {
                return null;
            }

            var combat = _runCombat + (_current is null ? TimeSpan.Zero : _current.LastActivity - _current.Start);
            var since = _runZone ?? _run.Start;
            return BuildSnapshot(_run, combat) with { TargetName = RunTitle(_run.LastActivity - since, _runFights), ZoneEnteredAt = _runZone };
        }
    }

    /// <summary>"Run 18:20 · 3 peleas".</summary>
    public static string RunTitle(TimeSpan sinceEntry, int fights) =>
        $"Run {(int)Math.Max(0, sinceEntry.TotalMinutes):00}:{Math.Max(0, sinceEntry.Seconds):00} · {(fights == 1 ? "1 pelea" : $"{fights} peleas")}";

    /// <summary>"Sesión · 3 peleas".</summary>
    public static string SessionTitle(int fights) => fights == 1 ? "Sesión · 1 pelea" : $"Sesión · {fights} peleas";

    public EncounterSnapshot? CurrentSnapshot()
    {
        lock (_gate)
        {
            return _current is null ? null : BuildSnapshot(_current);
        }
    }

    /// <summary>Id del encuentro en curso, sin armar el snapshot; <c>null</c> si no hay pelea.</summary>
    public Guid? CurrentEncounterId()
    {
        lock (_gate)
        {
            return _current?.Id;
        }
    }

    /// <summary>Registro del encuentro en curso o, si no hay, del último que terminó.</summary>
    public IReadOnlyList<CombatLogEntry> CombatLog()
    {
        lock (_gate)
        {
            return _current is null ? _lastLog : [.. _current.Log];
        }
    }

    private void OnDamage(DamageEvent e)
    {
        // Sin monto, o con uno imposible (MaxRealHit): no suma, no abre ni sostiene la pelea, no elige el objetivo
        // principal y no entra en la verificación ni en el daño recibido.
        if (e.Amount <= 0 || e.Amount > MaxRealHit)
        {
            return;
        }

        var source = Entities.Canonical(e.SourceId);
        var target = Entities.Canonical(e.TargetId);

        // Una invocación que pega antes de su spawn no tiene npcId todavía: se la trata como una sin dueño, no como jugador.
        var npcSource = Entities.IsNpc(source) || Entities.IsPresumedSummon(e.SourceId, e.SkillId, target);
        if (npcSource && source != target && Entities.IsNpc(target) && Entities.TryAdoptSummon(source, e.SkillId, out var owner))
        {
            source = owner; // invocación sin dueño que le pega a un monstruo con una skill de jugador
            npcSource = false;
        }

        // Con los dueños resueltos, la invocación que se pega a sí misma al vencer (su vida más 100.000, skill 16990004),
        // la que le pega a su dueño o el dueño que le pega a su invocación quedan de un jugador a sí mismo: no es daño a
        // un enemigo ni daño recibido.
        if (source == target && (source != e.SourceId || target != e.TargetId))
        {
            return;
        }

        if (npcSource)
        {
            // Invocación sin dueño que no se pudo atribuir (por ejemplo, dos jugadores de su clase en la party): no entra al
            // ranking ni sostiene la pelea, pero le baja la vida al monstruo, así que la verificación la suma, como a los
            // jugadores fuera del filtro. Solo con skill de clase y a otro NPC: los monstruos entre sí, las invocaciones que
            // se pegan a sí mismas y las curaciones del boss leídas como daño no son daño de jugadores. Volver acá no saltea
            // nada: con un objetivo NPC, OnDamageTaken no hace nada.
            if (source != target && Entities.IsNpc(target) && EntityRegistry.ClassOfSkill(e.SkillId) is not null
                && VerifyingFight(e.Timestamp, e.TargetId) is { } fight)
            {
                CountForVerification(fight, e);
                return;
            }

            OnDamageTaken(e, source);
            return;
        }

        Entities.ObserveSkill(source, e.SkillId);
        if (!InScope(source))
        {
            // Jugadores fuera del filtro no cuentan ni abren encuentros, pero le bajan la vida al objetivo: la verificación los suma.
            if (VerifyingFight(e.Timestamp, e.TargetId) is { } fight)
            {
                CountForVerification(fight, e);
            }

            return;
        }

        if (OpensEncounter(e.Timestamp, e.TargetId) && !CanOpenEncounter(source, target))
        {
            return;
        }

        var encounter = EnsureEncounter(e.Timestamp, e.TargetId);
        CountForVerification(encounter, e);
        var before = HealthPercent(e.TargetId);
        var overkill = Entities.ApplyDamage(e.TargetId, e.Amount);
        TrackPhases(e.TargetId, before, e.Timestamp);
        // Solo las invocaciones tienen dueño (por su spawn o adoptadas por clase): si el dueño reemplazó a quien pegó, el
        // golpe es de una invocación y se anota aparte para "Invocaciones separadas".
        var fromSummon = source != e.SourceId;
        RecordHit(encounter, source, e, overkill, fromSummon);
        RecordHit(_session!, source, e, overkill, fromSummon);
        RecordHit(_run!, source, e, overkill, fromSummon);
        encounter.AddLog(Entry(e, LogKind.Damage, source, Entities.Canonical(e.TargetId), e.SkillId, e.Amount, e.Modifiers));
    }

    /// <summary>Porcentajes de vida que marcan las fases de una pelea.</summary>
    internal static readonly int[] PhaseThresholds = [75, 50, 25];

    private double? HealthPercent(ulong target) =>
        Entities.HealthOf(target) is { MaxHp: > 0 } health ? 100.0 * health.Hp / health.MaxHp : null;

    /// <summary>
    /// Anota el segundo en que la vida del objetivo bajó de cada fase. Solo las fases por encima de la vida con que se lo
    /// vio por primera vez en la pelea: si arrancamos a capturar con el boss al 40 %, el 75 y el 50 no se inventan.
    /// </summary>
    private void TrackPhases(ulong target, double? before, DateTimeOffset at)
    {
        if (_current is not { } encounter || HealthPercent(target) is not { } now)
        {
            return;
        }

        if (!encounter.FirstHealthPercent.TryGetValue(target, out var first))
        {
            encounter.FirstHealthPercent[target] = first = before ?? now;
        }

        var phases = encounter.Phases.TryGetValue(target, out var list) ? list : encounter.Phases[target] = [];
        foreach (var threshold in PhaseThresholds)
        {
            if (first > threshold && now <= threshold && !phases.Exists(p => p.Percent == threshold))
            {
                phases.Add(new HealthPhase(threshold, Math.Max(0, (int)(at - encounter.Start).TotalSeconds)));
            }
        }
    }

    /// <summary>
    /// Suma el golpe a la verificación de su objetivo (solo NPCs): los de todos los jugadores, también los que el filtro
    /// deja fuera, y los de las invocaciones sin dueño que pegan con una skill de clase, aunque no entren al ranking. Con
    /// el primer golpe de la pelea sobre él se anota la vida que informó el servidor hasta ahí (ver
    /// <see cref="TargetVerification"/>).
    /// </summary>
    private void CountForVerification(Encounter encounter, DamageEvent e)
    {
        if (!Entities.IsNpc(Entities.Canonical(e.TargetId)))
        {
            return;
        }

        if (!encounter.Verifications.TryGetValue(e.TargetId, out var count))
        {
            count = new VerificationCount(Entities.ServerHealthOf(e.TargetId)?.Hp);
            encounter.Verifications[e.TargetId] = count;
        }

        count.AddHit(e.Amount);
    }

    private static void RecordHit(Encounter encounter, ulong source, DamageEvent e, long overkill, bool fromSummon)
    {
        var actor = encounter.Actor(source);
        actor.AddHit(e.SkillId, e.TargetId, e.Amount, e.Modifiers, fromSummon);
        actor.AddToTimeline((int)Math.Max((e.Timestamp - encounter.Start).TotalSeconds, 0), e.Amount, e.Modifiers.HasFlag(HitModifiers.DamageOverTime));
        actor.Overkill += overkill;
        encounter.RegisterDamage(e.TargetId, e.Amount, e.Timestamp);
    }

    /// <summary>La pelea actual y la sesión, para lo que se suma en las dos.</summary>
    private IEnumerable<Encounter> Running()
    {
        if (_current is not null)
        {
            yield return _current;
        }

        if (_session is not null)
        {
            yield return _session;
        }

        if (_run is not null)
        {
            yield return _run;
        }
    }

    /// <summary>Un monstruo le pega a un jugador: cuenta como daño recibido si hay pelea y el jugador entra en el filtro.</summary>
    private void OnDamageTaken(DamageEvent e, ulong npc)
    {
        var target = Entities.Canonical(e.TargetId);
        if (_current is null || Entities.IsNpc(target) || !InScope(target))
        {
            return;
        }

        // Sin un Tick intermedio, un golpe tardío tampoco revive una pelea vencida.
        if (IsIdle(e.Timestamp))
        {
            Finish();
            return;
        }

        // Solo los golpes de otro NPC sostienen la pelea en una pausa del daño hecho. Los del objetivo principal no:
        // Combat Analysis cortó a Destroyer Tarka en una pausa de 9,7 s aunque el boss le siguió pegando al local. En la
        // comparación del 2026-10-17 (en party, sin Combat Analysis; la referencia es otro medidor) la pelea contra
        // 2300371 sigue en pausas de 8,5 y 9,1 s en las que solo pegan sus entidades (npc 2920343 y 2920347). Pasada la
        // inactividad configurada (en la pausa de boss), solo los de un NPC que ya es parte de la pelea: los de otro boss
        // que hace el pull, o de sus adds, no la sostienen, y el primer golpe del grupo a ellos abre otra pelea.
        var sustains = (_current.MainTarget is not { } main || (main != npc && main != e.SourceId))
            && (e.Timestamp - _current.LastCombatActivity <= _idleTimeout || IsPartOf(_current, npc) || IsPartOf(_current, e.SourceId));
        foreach (var encounter in Running())
        {
            if (sustains)
            {
                encounter.RegisterCombatActivity(e.Timestamp);
            }

            var actor = encounter.Actor(target);
            actor.DamageTaken += e.Amount;
            actor.HitsTaken++;
            if (!actor.TakenBySkill.TryGetValue(e.SkillId, out var skill))
            {
                skill = new TakenSkillStats();
                actor.TakenBySkill[e.SkillId] = skill;
            }

            skill.Damage += e.Amount;
            skill.Hits++;
        }

        _current.AddLog(Entry(e, LogKind.Damage, npc, target, e.SkillId, e.Amount, e.Modifiers));
    }

    private CombatLogEntry Entry(CombatEvent e, LogKind kind, ulong source, ulong target, uint skill, long amount, HitModifiers modifiers = HitModifiers.None) =>
        new(e.Timestamp, kind, source, Entities.DisplayName(source), target, Entities.DisplayName(target), skill, amount, modifiers);

    private void OnHeal(HealEvent e)
    {
        if (_current is null || e.Amount <= 0)
        {
            return;
        }

        var source = Entities.Canonical(e.SourceId);
        if (!Entities.IsNpc(source))
        {
            Entities.ObserveSkill(source, e.SkillId);
        }

        if (!Entities.IsNpc(source) && InScope(source))
        {
            foreach (var encounter in Running())
            {
                encounter.Actor(source).Healing += e.Amount;
            }

            _current.AddLog(Entry(e, LogKind.Heal, source, Entities.Canonical(e.TargetId), e.SkillId, e.Amount));
        }
    }

    private void OnDeath(DeathEvent e)
    {
        // La muerte del objetivo principal cierra la pelea con este mismo evento, y su cuenta registra la muerte. Sin
        // preguntar si la pelea sigue viva: el registro ya lo marcó muerto, y un boss muerto no sostiene la pausa de boss.
        var killsMainTarget = _current is not null && _splitEncounters && options.EndOnMainTargetDeath
            && _current.MainTarget == e.EntityId && Entities.IsNpc(e.EntityId);
        if ((killsMainTarget ? _current : VerifyingFight(e.Timestamp, e.EntityId)) is { } fight
            && fight.Verifications.TryGetValue(e.EntityId, out var count))
        {
            count.Kill();
        }

        var dead = Entities.Canonical(e.EntityId);
        // El daño de una invocación es del dueño; la muerte del NPC físico no es una muerte del jugador.
        if (_current is not null && !Entities.IsNpc(e.EntityId) && !Entities.IsNpc(dead) && InScope(dead))
        {
            // Los golpes que recibió en los últimos segundos: el último es el que lo mató.
            var since = e.Timestamp - DeathRecap.Window;
            var lastHits = _current.Log
                .Where(l => l.TargetId == dead && l.Kind == LogKind.Damage && l.Timestamp >= since)
                .ToArray();
            var recap = new DeathRecap(e.Timestamp, dead, Entities.DisplayName(dead), lastHits);
            foreach (var encounter in Running())
            {
                encounter.Actor(dead).Deaths++;
                encounter.Deaths.Add(recap);
            }

            _current.AddLog(Entry(e, LogKind.Death, dead, dead, 0, 0));
        }

        if (killsMainTarget && _current is not null)
        {
            _current.TargetKilled = true;
            _current.LastActivity = e.Timestamp;
            Finish();
        }
    }

    /// <summary>
    /// Pasó la inactividad configurada o, si el objetivo principal es un boss del catálogo vivo que no se reseteó, la pausa
    /// de boss. Para un evento de un NPC que no es parte de la pelea (<see cref="IsPartOf"/>) alcanza con la inactividad
    /// configurada. Ferocious Horn Nuakum tiene pausas de fase de 8,3 a 8,6 s sin daño hecho (2026-10-19 y 2026-10-29): con
    /// 8 s la pelea quedaba en dos, y en la del 2026-10-29 la segunda arrancaba con la misma vida del boss con que terminaba
    /// la primera. Destroyer Tarka también está marcado como boss: sus 3 s de intento y el kill, 9,7 s después, quedan en
    /// una sola pelea si el primer golpe después de la pausa va a algo de ella, aunque Combat Analysis los separa.
    /// </summary>
    /// <param name="involved">El NPC del evento que se evalúa (al que se le pega, o del que llega la vida o la muerte), si lo hay.</param>
    private bool IsIdle(DateTimeOffset at, ulong? involved = null)
    {
        if (!_splitEncounters || _current is null)
        {
            return false;
        }

        // Primero la inactividad configurada: el objetivo principal se calcula solo pasada esa, no en cada golpe.
        var quiet = at - _current.LastCombatActivity;
        return quiet > _idleTimeout
            && (quiet > options.BossPauseTimeout || !IsLiveBoss(_current) || involved is { } other && !IsPartOf(_current, other));
    }

    private bool IsLiveBoss(Encounter encounter) =>
        encounter.MainTarget is { } target
        && IsBoss(target) && !Entities.IsDeadNpc(target) && !encounter.ResetTargets.Contains(target);

    /// <summary>
    /// Parte de la pelea: un NPC que recibió daño del filtro en ella (la entidad cruda, como en
    /// <see cref="Encounter.DamageTaken"/>). Pasada la inactividad configurada, solo lo que ya es parte de la pelea la sigue
    /// en la pausa de boss: pegarle a otra cosa abre otra pelea, como antes, y que otra cosa le pegue a la party no la corta
    /// ni la sostiene. En las pausas medidas (Nuakum, Kwapo, Duanka), el grupo volvió a pegarle al boss. El 2026-10-29,
    /// Silver Blade Rotan quedó vivo 13,8 s antes del pull de Kromede's Desire: si no se reseteó, ese pull, o sus adds, se
    /// sumaban a su pelea. La copia del catálogo del usuario marca boss también a entidades de mecánica, como las de Atiel
    /// (npc 2920445 y 2920446), que le pegan a la party en sus pausas sin que nadie les pegue. Un jugador nunca es parte: el
    /// "daño" a sí mismo o a un compañero (costos de vida) también queda en <see cref="Encounter.DamageTaken"/>. Un monstruo
    /// sin spawn capturado (sin npcId) tampoco: para él vale la regla de antes.
    /// </summary>
    private bool IsPartOf(Encounter encounter, ulong entity) =>
        Entities.IsNpc(Entities.Canonical(entity)) && encounter.DamageTaken.ContainsKey(entity);

    private bool IsBoss(ulong entity) =>
        Entities.Profile(entity).NpcId is { } npc && Entities.Npcs.Of(npc) is { IsBoss: true };

    /// <summary>
    /// Sigue la vida que informa el servidor de los objetivos de la pelea: volver al máximo después de verse por debajo es
    /// un reset (wipe, o el boss se fue de rango y se regeneró). Si es el objetivo principal, la pelea vuelve a la
    /// inactividad configurada; se siguen todos porque el objetivo principal puede cambiar. La lectura llena antes de verlo
    /// herido no cuenta: en el pull puede llegar después del primer golpe.
    /// </summary>
    private void TrackReset(ulong target)
    {
        if (_current is not { } encounter || !encounter.DamageTaken.ContainsKey(target)
            || Entities.ServerHealthOf(target) is not { MaxHp: > 0 } health)
        {
            return;
        }

        if (health.Hp < health.MaxHp)
        {
            encounter.HurtTargets.Add(target);
            encounter.ResetTargets.Remove(target);
        }
        else if (encounter.HurtTargets.Contains(target))
        {
            encounter.ResetTargets.Add(target);
        }
    }

    /// <summary>
    /// Otra entidad del mismo NPC que el objetivo principal, sin herir, es el boss que volvió: un reset. Atiel (2026-10-20 y
    /// 21) reaparece como otra entidad en cada intento, con la vida llena, y de la vieja no llega la muerte ni una lectura
    /// llena: sin esto, la pelea del wipe seguía abierta hasta el primer golpe del intento siguiente, y se unía con él si
    /// ese golpe iba a algo que ya era parte de ella o si un add de ella le pegaba a la party en el hueco. Una que llega
    /// herida es una copia, no un reset.
    /// </summary>
    private void TrackRespawn(EntitySpawnEvent spawn)
    {
        if (_current is not { } encounter || spawn.NpcId is not { } npc || Entities.Npcs.Of(npc) is not { IsBoss: true }
            || encounter.MainTarget is not { } main || main == spawn.EntityId || Entities.Profile(main).NpcId != npc
            || spawn is { Hp: { } hp, MaxHp: { } maxHp } && hp < maxHp)
        {
            return;
        }

        encounter.ResetTargets.Add(main);
    }

    private bool OpensEncounter(DateTimeOffset at, ulong? involved = null) =>
        _current is null || IsIdle(at, involved);

    /// <summary>La pelea en curso si sigue viva en <paramref name="at"/>: pasada la inactividad, el próximo golpe abre otra.</summary>
    private Encounter? LiveFight(DateTimeOffset at, ulong? involved = null) => OpensEncounter(at, involved) ? null : _current;

    /// <summary>
    /// La pelea en curso para lo que solo va a la verificación de <paramref name="entity"/> (lecturas de vida, muertes,
    /// golpes de jugadores fuera del filtro o de invocaciones sin dueño). Si para él ya no está viva, su cuenta se congela:
    /// en la pausa de boss la pelea puede seguir para otros, y lo que le llegue después no se vería entero. La muerte del
    /// objetivo principal no pasa por acá (ver <see cref="OnDeath"/>).
    /// </summary>
    private Encounter? VerifyingFight(DateTimeOffset at, ulong entity)
    {
        if (LiveFight(at, entity) is { } fight)
        {
            return fight;
        }

        if (_current is not null && _current.Verifications.TryGetValue(entity, out var count))
        {
            count.Freeze();
        }

        return null;
    }

    /// <summary>
    /// Solo un golpe a un monstruo vivo abre una pelea. Al morir el boss siguen llegando golpes que ya estaban
    /// en vuelo, ticks sobre el cadáver y "daño" de jugador a jugador (costos de vida, efectos propios): antes
    /// abrían una pelea fantasma de décimas de segundo que tapaba el resultado del boss.
    /// </summary>
    private bool CanOpenEncounter(ulong source, ulong target) =>
        target != source && !Entities.IsKnownPlayer(target) && !Entities.IsDeadNpc(target);

    private Encounter EnsureEncounter(DateTimeOffset at, ulong? involved = null)
    {
        if (IsIdle(at, involved))
        {
            Finish();
        }

        if (_current is null)
        {
            _current = new Encounter(at) { ZoneEnteredAt = Entities.ZoneEnteredAt };
            _session ??= new Encounter(at);
            _sessionFights++;
            if (_run is null || _runZone != Entities.ZoneEnteredAt)
            {
                _run = new Encounter(at);
                _runCombat = TimeSpan.Zero;
                _runFights = 0;
                _runZone = Entities.ZoneEnteredAt;
            }

            _runFights++;
        }

        return _current;
    }

    private bool InScope(ulong canonicalSource)
    {
        if (_scope == ActorScope.Everyone || Entities.LocalPlayerId is not { } local || canonicalSource == local)
        {
            return true;
        }

        return _scope == ActorScope.Party && Entities.WasInPartyThisZone(canonicalSource);
    }

    private void Finish()
    {
        if (_current is null)
        {
            return;
        }

        var snapshot = BuildSnapshot(_current);
        _sessionCombat += _current.LastActivity - _current.Start;
        _runCombat += _current.LastActivity - _current.Start;
        _lastLog = _current.Log;
        _current = null;
        EncounterFinished?.Invoke(snapshot);
    }

    /// <param name="combatTime">Duración a usar para el DPS; por defecto, del primer al último golpe.</param>
    private EncounterSnapshot BuildSnapshot(Encounter encounter, TimeSpan? combatTime = null)
    {
        var total = encounter.Actors.Values.Sum(a => a.Damage);
        var seconds = Math.Max((combatTime ?? (encounter.LastActivity - encounter.Start)).TotalSeconds, 1);
        var target = encounter.MainTarget;
        TimeSpan? targetDuration = target is { } mt && encounter.TargetWindows.TryGetValue(mt, out var window)
            ? window.Last - window.First
            : null;
        var targetSeconds = Math.Max(targetDuration?.TotalSeconds ?? 0, 1);
        var targetNpc = target is { } tn ? Entities.Profile(tn).NpcId : null;

        var actors = encounter.Actors
            .Select(kv =>
            {
                var (id, stats) = (kv.Key, kv.Value);
                var profile = Entities.Profile(id);
                var targetDamage = target is { } t0 ? stats.DamageByTarget.GetValueOrDefault(t0) : 0;
                var skills = stats.Skills
                    .Select(s => new SkillSnapshot(s.Key.SkillId, s.Value.Damage, s.Value.Hits, s.Value.Crits, s.Value.MaxHit, Share(s.Value.Damage, stats.Damage), s.Value.Counts.Snapshot(), s.Value.PeriodicDamage, s.Key.FromSummon))
                    .OrderByDescending(s => s.Damage)
                    .ToArray();
                long[] timeline = [.. stats.DamageBySecond];
                // Sin DoT sería igual al Timeline: no se guarda dos veces.
                long[]? direct = stats.PeriodicDamage > 0 ? [.. stats.DirectDamageBySecond] : null;
                // Sin daño recibido no hay desglose: null, como en las peleas guardadas antes de que existiera.
                TakenSkillSnapshot[]? taken = stats.TakenBySkill.Count == 0
                    ? null
                    : [.. stats.TakenBySkill.Select(s => new TakenSkillSnapshot(s.Key, s.Value.Damage, s.Value.Hits)).OrderByDescending(s => s.Damage)];
                return new ActorSnapshot(
                    id,
                    Entities.DisplayName(id),
                    profile.ClassId,
                    id == Entities.LocalPlayerId,
                    stats.Damage,
                    stats.Damage / seconds,
                    Share(stats.Damage, total),
                    stats.Hits,
                    stats.Crits,
                    stats.MaxHit,
                    stats.Healing,
                    skills,
                    target is { } t1 && stats.CountsByTarget.TryGetValue(t1, out var counts) ? counts.Snapshot() : HitCounts.Empty,
                    targetDamage,
                    targetDamage / targetSeconds,
                    stats.PeriodicDamage,
                    // Con la misma actividad que ACTIVO y TIEMPO MUERTO (FightActivity.Of): los ticks de DoT no son tiempo activo.
                    // Topeado con el tiempo del DPS: en la sesión y la run, los ratos entre peleas no dejan segundos de más.
                    FightActivity.ActiveDps(direct, timeline, seconds),
                    stats.Overkill,
                    timeline,
                    stats.DamageTaken,
                    stats.HitsTaken,
                    stats.Deaths,
                    profile.ItemLevel,
                    profile.CombatPower,
                    direct,
                    taken);
            })
            .OrderByDescending(a => a.Damage)
            .ToArray();

        // Solo la pelea tiene cuentas: la sesión y la run quedan sin verificación.
        TargetVerification[] verifications =
        [
            .. encounter.Verifications
                .Select(kv => kv.Value.Result(kv.Key, Entities.Profile(kv.Key).NpcId, Entities.ServerHealthOf(kv.Key)?.MaxHp))
                .OfType<TargetVerification>()
                .OrderByDescending(v => v.Counted),
        ];

        return new EncounterSnapshot(
            encounter.Id,
            encounter.Start,
            combatTime is { } combat ? encounter.Start + combat : encounter.LastActivity,
            // Los monstruos no traen nombre en el paquete: sale de data/npcs.json o queda null, no "#entidad".
            target is { } t ? Entities.NameOf(t) : null,
            encounter.TargetKilled,
            total,
            actors,
            targetDuration,
            target is { } th ? Entities.HealthOf(th) : null,
            [.. encounter.Deaths],
            targetNpc,
            targetNpc is { } npc && Entities.Npcs.Of(npc) is { IsBoss: true },
            [.. encounter.Marks],
            encounter.ZoneEnteredAt,
            targetNpc is { } enrageNpc ? Entities.Npcs.Of(enrageNpc)?.EnrageSeconds : null,
            target is { } tp && encounter.Phases.TryGetValue(tp, out var phases) && phases.Count > 0 ? [.. phases] : null,
            verifications.Length > 0 ? verifications : null);
    }

    private static double Share(long part, long total) => total == 0 ? 0 : (double)part / total;
}
