using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.Combat;

public enum LogKind
{
    Damage,
    Heal,
    Death,
}

/// <summary>Una línea del registro de la pelea, con los nombres que se conocían en ese momento.</summary>
public sealed record CombatLogEntry(
    DateTimeOffset Timestamp,
    LogKind Kind,
    ulong SourceId,
    string SourceName,
    ulong TargetId,
    string TargetName,
    uint SkillId,
    long Amount,
    HitModifiers Modifiers = HitModifiers.None);

/// <summary>Una muerte de un jugador: cuándo y los golpes que recibió en los segundos previos (el último es el que lo mató).</summary>
public sealed record DeathRecap(DateTimeOffset Timestamp, ulong ActorId, string Name, IReadOnlyList<CombatLogEntry> LastHits)
{
    /// <summary>Segundos antes de la muerte que se guardan en <see cref="LastHits"/>.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    public CombatLogEntry? KillingBlow => LastHits.Count == 0 ? null : LastHits[^1];
}
