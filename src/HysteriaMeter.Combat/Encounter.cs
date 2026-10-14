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

    public Dictionary<uint, SkillStats> Skills { get; } = [];

    public void AddHit(uint skillId, long amount, HitModifiers modifiers)
    {
        var critical = modifiers.HasFlag(HitModifiers.Critical);
        Damage += amount;
        Hits++;
        Crits += critical ? 1 : 0;
        MaxHit = Math.Max(MaxHit, amount);

        if (!Skills.TryGetValue(skillId, out var skill))
        {
            skill = new SkillStats();
            Skills[skillId] = skill;
        }

        skill.Damage += amount;
        skill.Hits++;
        skill.Crits += critical ? 1 : 0;
        skill.MaxHit = Math.Max(skill.MaxHit, amount);
    }
}

internal sealed class SkillStats
{
    public long Damage { get; set; }

    public int Hits { get; set; }

    public int Crits { get; set; }

    public long MaxHit { get; set; }
}
