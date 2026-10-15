using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat;

/// <summary>Acumuladores mutables de un encuentro en curso. Solo los toca el <see cref="CombatEngine"/>.</summary>
internal sealed class Encounter(DateTimeOffset start)
{
    public Guid Id { get; } = Guid.NewGuid();

    public DateTimeOffset Start { get; } = start;

    public DateTimeOffset LastActivity { get; set; } = start;

    public Dictionary<ulong, ActorStats> Actors { get; } = [];

    /// <summary>Daño recibido por cada objetivo; el que más recibió es el "objetivo principal" (boss).</summary>
    public Dictionary<ulong, long> DamageTaken { get; } = [];

    public bool TargetKilled { get; set; }

    public ulong? MainTarget => DamageTaken.Count == 0 ? null : DamageTaken.MaxBy(kv => kv.Value).Key;

    /// <summary>Primer y último golpe a cada objetivo: Combat Analysis mide la pelea así.</summary>
    public Dictionary<ulong, (DateTimeOffset First, DateTimeOffset Last)> TargetWindows { get; } = [];

    public void RegisterDamage(ulong target, long amount, DateTimeOffset at)
    {
        DamageTaken[target] = DamageTaken.GetValueOrDefault(target) + amount;
        TargetWindows[target] = TargetWindows.TryGetValue(target, out var window) ? (window.First, at) : (at, at);
        LastActivity = at;
    }

    public ActorStats Actor(ulong id)
    {
        if (!Actors.TryGetValue(id, out var stats))
        {
            stats = new ActorStats();
            Actors[id] = stats;
        }

        return stats;
    }
}

internal sealed class ActorStats
{
    public long Damage { get; private set; }

    public int Hits { get; private set; }

    public int Crits { get; private set; }

    public long MaxHit { get; private set; }

    public long Healing { get; set; }

    /// <summary>Daño que pasó de la vida que le quedaba al objetivo. Se informa aparte; no se descuenta del daño.</summary>
    public long Overkill { get; set; }

    public long PeriodicDamage { get; private set; }

    /// <summary>Tiempo con actividad: suma de los huecos entre golpes, cada uno topeado en <see cref="ActiveGap"/>.</summary>
    public TimeSpan ActiveTime { get; private set; }

    private DateTimeOffset? _lastHitAt;

    internal static readonly TimeSpan ActiveGap = TimeSpan.FromSeconds(3);

    public Dictionary<uint, SkillStats> Skills { get; } = [];

    public Dictionary<ulong, long> DamageByTarget { get; } = [];

    /// <summary>Golpes por modificador separados por objetivo: Combat Analysis cuenta solo los del objetivo elegido.</summary>
    public Dictionary<ulong, HitCounter> CountsByTarget { get; } = [];

    public void AddHit(uint skillId, ulong targetId, long amount, HitModifiers modifiers, DateTimeOffset at)
    {
        if (_lastHitAt is { } last && at > last)
        {
            var gap = at - last;
            ActiveTime += gap < ActiveGap ? gap : ActiveGap;
        }

        _lastHitAt = at;
        DamageByTarget[targetId] = DamageByTarget.GetValueOrDefault(targetId) + amount;
        if (!CountsByTarget.TryGetValue(targetId, out var counts))
        {
            counts = new HitCounter();
            CountsByTarget[targetId] = counts;
        }

        counts.Add(modifiers);
        // Como Combat Analysis: los ticks de DoT suman daño pero no cuentan como golpe.
        var periodic = modifiers.HasFlag(HitModifiers.DamageOverTime);
        var hit = periodic ? 0 : 1;
        var critical = modifiers.HasFlag(HitModifiers.Critical);
        Damage += amount;
        PeriodicDamage += periodic ? amount : 0;
        Hits += hit;
        Crits += critical ? 1 : 0;
        MaxHit = periodic ? MaxHit : Math.Max(MaxHit, amount);

        if (!Skills.TryGetValue(skillId, out var skill))
        {
            skill = new SkillStats();
            Skills[skillId] = skill;
        }

        skill.Counts.Add(modifiers);
        skill.Damage += amount;
        skill.PeriodicDamage += periodic ? amount : 0;
        skill.Hits += hit;
        skill.Crits += critical ? 1 : 0;
        skill.MaxHit = periodic ? skill.MaxHit : Math.Max(skill.MaxHit, amount);
    }
}

internal sealed class SkillStats
{
    public long Damage { get; set; }

    public long PeriodicDamage { get; set; }

    public int Hits { get; set; }

    public int Crits { get; set; }

    public long MaxHit { get; set; }

    public HitCounter Counts { get; } = new();
}

/// <summary>Cuenta golpes por modificador, igual que las columnas de Combat Analysis. Los ticks de DoT no son golpes.</summary>
internal sealed class HitCounter
{
    private int _hits;
    private int _crits;
    private int _perfect;
    private int _double;
    private int _front;
    private int _back;
    private int _multi;

    public void Add(HitModifiers modifiers)
    {
        if (modifiers.HasFlag(HitModifiers.DamageOverTime))
        {
            return;
        }

        _hits++;
        _crits += modifiers.HasFlag(HitModifiers.Critical) ? 1 : 0;
        _perfect += modifiers.HasFlag(HitModifiers.Perfect) ? 1 : 0;
        _double += modifiers.HasFlag(HitModifiers.DoubleHit) ? 1 : 0;
        _front += modifiers.HasFlag(HitModifiers.FrontAttack) ? 1 : 0;
        _back += modifiers.HasFlag(HitModifiers.BackAttack) ? 1 : 0;
        _multi += modifiers.HasFlag(HitModifiers.MultiHit) ? 1 : 0;
    }

    public HitCounts Snapshot() => new(_hits, _crits, _perfect, _double, _front, _back, _multi);
}
