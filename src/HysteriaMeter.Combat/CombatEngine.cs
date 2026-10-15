using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat;

public sealed record CombatEngineOptions
{
    /// <summary>Sin daño durante este tiempo, el encuentro se cierra.</summary>
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
    private ActorScope _scope = ActorScope.Party;
    private bool _splitEncounters = true;

    public CombatEngine() : this(new CombatEngineOptions())
    {
    }

    public EntityRegistry Entities { get; } = new();

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
            }
        }
    }

    /// <summary>Avanza el reloj: cierra el encuentro si pasó el tiempo de inactividad.</summary>
    public void Tick(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_splitEncounters && _current is not null && now - _current.LastActivity > options.IdleTimeout)
            {
                Finish();
            }
        }
    }

    /// <summary>Cierra a mano el encuentro actual (botón "reset").</summary>
    public void Reset()
    {
        lock (_gate)
        {
            Finish();
        }
    }

    public EncounterSnapshot? CurrentSnapshot()
    {
        lock (_gate)
        {
            return _current is null ? null : BuildSnapshot(_current);
        }
    }

    private void OnDamage(DamageEvent e)
    {
        if (e.Amount <= 0)
        {
            return;
        }

        var source = Entities.Canonical(e.SourceId);
        if (Entities.IsNpc(source) || !InScope(source))
        {
            return; // monstruos y jugadores fuera del filtro no cuentan ni abren encuentros
        }

        var encounter = EnsureEncounter(e.Timestamp);
        var actor = encounter.Actor(source);
        actor.AddHit(e.SkillId, e.TargetId, e.Amount, e.Modifiers, e.Timestamp);
        actor.AddToTimeline((int)Math.Max((e.Timestamp - encounter.Start).TotalSeconds, 0), e.Amount);
        actor.Overkill += Entities.ApplyDamage(e.TargetId, e.Amount);
        encounter.RegisterDamage(e.TargetId, e.Amount, e.Timestamp);
    }

    private void OnHeal(HealEvent e)
    {
        if (_current is null || e.Amount <= 0)
        {
            return;
        }

        var source = Entities.Canonical(e.SourceId);
        if (!Entities.IsNpc(source) && InScope(source))
        {
            _current.Actor(source).Healing += e.Amount;
        }
    }

    private void OnDeath(DeathEvent e)
    {
        if (_current is not null && _splitEncounters && options.EndOnMainTargetDeath && _current.MainTarget == e.EntityId && Entities.IsNpc(e.EntityId))
        {
            _current.TargetKilled = true;
            _current.LastActivity = e.Timestamp;
            Finish();
        }
    }

    private Encounter EnsureEncounter(DateTimeOffset at)
    {
        if (_splitEncounters && _current is not null && at - _current.LastActivity > options.IdleTimeout)
        {
            Finish();
        }

        return _current ??= new Encounter(at);
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
        _current = null;
        EncounterFinished?.Invoke(snapshot);
    }

    private EncounterSnapshot BuildSnapshot(Encounter encounter)
    {
        var total = encounter.Actors.Values.Sum(a => a.Damage);
        var seconds = Math.Max((encounter.LastActivity - encounter.Start).TotalSeconds, 1);
        var target = encounter.MainTarget;
        TimeSpan? targetDuration = target is { } mt && encounter.TargetWindows.TryGetValue(mt, out var window)
            ? window.Last - window.First
            : null;
        var targetSeconds = Math.Max(targetDuration?.TotalSeconds ?? 0, 1);

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
                    [.. stats.DamageBySecond]);
            })
            .OrderByDescending(a => a.Damage)
            .ToArray();

        return new EncounterSnapshot(
            encounter.Id,
            encounter.Start,
            encounter.LastActivity,
            target is { } t ? Entities.DisplayName(t) : null,
            encounter.TargetKilled,
            total,
            actors,
            targetDuration,
            target is { } th ? Entities.HealthOf(th) : null);
    }

    private static double Share(long part, long total) => total == 0 ? 0 : (double)part / total;
}
