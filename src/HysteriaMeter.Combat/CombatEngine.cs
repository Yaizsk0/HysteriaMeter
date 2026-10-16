using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat;

public sealed record CombatEngineOptions
{
    /// <summary>Sin daño durante este tiempo, el encuentro se cierra (valor inicial de <see cref="CombatEngine.IdleTimeout"/>).</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(8);

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

    /// <summary>Sin daño durante este tiempo, el encuentro se cierra. Se puede cambiar en caliente.</summary>
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
                    break;
            }
        }
    }

    /// <summary>Avanza el reloj: cierra el encuentro si pasó el tiempo de inactividad.</summary>
    public void Tick(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_splitEncounters && _current is not null && now - _current.LastActivity > _idleTimeout)
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
        if (e.Amount <= 0)
        {
            return;
        }

        var source = Entities.Canonical(e.SourceId);
        if (Entities.IsNpc(source) && Entities.IsNpc(Entities.Canonical(e.TargetId))
            && Entities.TryAdoptSummon(source, e.SkillId, out var owner))
        {
            source = owner; // invocación sin dueño que le pega a un monstruo con una skill de jugador
        }

        if (Entities.IsNpc(source))
        {
            OnDamageTaken(e, source);
            return;
        }

        Entities.ObserveSkill(source, e.SkillId);
        if (!InScope(source))
        {
            return; // jugadores fuera del filtro no cuentan ni abren encuentros
        }

        if (OpensEncounter(e.Timestamp) && !CanOpenEncounter(source, Entities.Canonical(e.TargetId)))
        {
            return;
        }

        var encounter = EnsureEncounter(e.Timestamp);
        var before = HealthPercent(e.TargetId);
        var overkill = Entities.ApplyDamage(e.TargetId, e.Amount);
        TrackPhases(e.TargetId, before, e.Timestamp);
        RecordHit(encounter, source, e, overkill);
        RecordHit(_session!, source, e, overkill);
        RecordHit(_run!, source, e, overkill);
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

    private static void RecordHit(Encounter encounter, ulong source, DamageEvent e, long overkill)
    {
        var actor = encounter.Actor(source);
        actor.AddHit(e.SkillId, e.TargetId, e.Amount, e.Modifiers, e.Timestamp);
        actor.AddToTimeline((int)Math.Max((e.Timestamp - encounter.Start).TotalSeconds, 0), e.Amount);
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

        foreach (var encounter in Running())
        {
            var actor = encounter.Actor(target);
            actor.DamageTaken += e.Amount;
            actor.HitsTaken++;
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
        var dead = Entities.Canonical(e.EntityId);
        if (_current is not null && !Entities.IsNpc(dead) && InScope(dead))
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

        if (_current is not null && _splitEncounters && options.EndOnMainTargetDeath && _current.MainTarget == e.EntityId && Entities.IsNpc(e.EntityId))
        {
            _current.TargetKilled = true;
            _current.LastActivity = e.Timestamp;
            Finish();
        }
    }

    private bool OpensEncounter(DateTimeOffset at) =>
        _current is null || (_splitEncounters && at - _current.LastActivity > _idleTimeout);

    /// <summary>
    /// Solo un golpe a un monstruo vivo abre una pelea. Al morir el boss siguen llegando golpes que ya estaban
    /// en vuelo, ticks sobre el cadáver y "daño" de jugador a jugador (costos de vida, efectos propios): antes
    /// abrían una pelea fantasma de décimas de segundo que tapaba el resultado del boss.
    /// </summary>
    private bool CanOpenEncounter(ulong source, ulong target) =>
        target != source && !Entities.IsKnownPlayer(target) && !Entities.IsDeadNpc(target);

    private Encounter EnsureEncounter(DateTimeOffset at)
    {
        if (_splitEncounters && _current is not null && at - _current.LastActivity > _idleTimeout)
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

        return _scope == ActorScope.Party && Entities.IsPartyMember(canonicalSource);
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
                    .Select(s => new SkillSnapshot(s.Key, s.Value.Damage, s.Value.Hits, s.Value.Crits, s.Value.MaxHit, Share(s.Value.Damage, stats.Damage), s.Value.Counts.Snapshot(), s.Value.PeriodicDamage))
                    .OrderByDescending(s => s.Damage)
                    .ToArray();
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
                    stats.Damage / Math.Max(stats.ActiveTime.TotalSeconds, 1),
                    stats.Overkill,
                    [.. stats.DamageBySecond],
                    stats.DamageTaken,
                    stats.HitsTaken,
                    stats.Deaths,
                    profile.ItemLevel,
                    profile.CombatPower);
            })
            .OrderByDescending(a => a.Damage)
            .ToArray();

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
            target is { } tp && encounter.Phases.TryGetValue(tp, out var phases) && phases.Count > 0 ? [.. phases] : null);
    }

    private static double Share(long part, long total) => total == 0 ? 0 : (double)part / total;
}
