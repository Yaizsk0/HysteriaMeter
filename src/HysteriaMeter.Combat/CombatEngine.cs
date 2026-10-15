using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat;

public sealed record CombatEngineOptions
{
    /// <summary>Sin daño durante este tiempo, el encuentro se cierra.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>Cerrar el encuentro cuando muere el objetivo principal.</summary>
    public bool EndOnMainTargetDeath { get; init; } = true;
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

    public CombatEngine() : this(new CombatEngineOptions())
    {
    }

    public EntityRegistry Entities { get; } = new();

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
            if (_current is not null && now - _current.LastActivity > options.IdleTimeout)
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
        if (Entities.IsNpc(source))
        {
            return; // daño que nos hacen los monstruos: fuera del ranking
        }

        var encounter = EnsureEncounter(e.Timestamp);
        encounter.Actor(source).AddHit(e.SkillId, e.TargetId, e.Amount, e.Modifiers);
        encounter.RegisterDamage(e.TargetId, e.Amount, e.Timestamp);
    }

    private void OnHeal(HealEvent e)
    {
        if (_current is null || e.Amount <= 0)
        {
            return;
        }

        var source = Entities.Canonical(e.SourceId);
        if (!Entities.IsNpc(source))
        {
            _current.Actor(source).Healing += e.Amount;
        }
    }

    private void OnDeath(DeathEvent e)
    {
        if (_current is not null && options.EndOnMainTargetDeath && _current.MainTarget == e.EntityId && Entities.IsNpc(e.EntityId))
        {
            _current.TargetKilled = true;
            _current.LastActivity = e.Timestamp;
            Finish();
        }
    }

    private Encounter EnsureEncounter(DateTimeOffset at)
    {
        if (_current is not null && at - _current.LastActivity > options.IdleTimeout)
        {
            Finish();
        }

        return _current ??= new Encounter(at);
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
                    .Select(s => new SkillSnapshot(s.Key, s.Value.Damage, s.Value.Hits, s.Value.Crits, s.Value.MaxHit, Share(s.Value.Damage, stats.Damage), s.Value.Counts.Snapshot()))
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
                    stats.Counts.Snapshot(),
                    targetDamage,
                    targetDamage / targetSeconds);
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
            targetDuration);
    }

    private static double Share(long part, long total) => total == 0 ? 0 : (double)part / total;
}
