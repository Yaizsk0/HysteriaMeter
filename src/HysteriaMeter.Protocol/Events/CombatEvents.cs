namespace HysteriaMeter.Protocol.Events;

/// <summary>Evento de dominio producido al decodificar un paquete. Los IDs son los crudos del servidor.</summary>
public abstract record CombatEvent(DateTimeOffset Timestamp);

/// <summary>El servidor identificó a nuestro personaje.</summary>
public sealed record LocalPlayerEvent(DateTimeOffset Timestamp, ulong EntityId, string? Name) : CombatEvent(Timestamp);

/// <summary>
/// Aparece una entidad. <paramref name="OwnerId"/> se completa para invocaciones / entidades de combate transitorias.
/// Algunas invocaciones no traen el ID del dueño sino su nombre (<paramref name="OwnerName"/>); el motor lo
/// resuelve contra los nombres de jugadores que conoce.
/// </summary>
public sealed record EntitySpawnEvent(DateTimeOffset Timestamp, ulong EntityId, ulong? OwnerId, uint? NpcId, string? OwnerName = null) : CombatEvent(Timestamp);

/// <summary>Nombre / clase de un jugador. Puede llegar tarde o no llegar.</summary>
public sealed record EntityInfoEvent(DateTimeOffset Timestamp, ulong EntityId, string Name, int? ClassId) : CombatEvent(Timestamp);

[Flags]
public enum HitModifiers
{
    None = 0,
    Critical = 1 << 0,
    BackAttack = 1 << 1,
    Parried = 1 << 2,
    Blocked = 1 << 3,
    Evaded = 1 << 4,
    DamageOverTime = 1 << 5,
}

public sealed record DamageEvent(
    DateTimeOffset Timestamp,
    ulong SourceId,
    ulong TargetId,
    uint SkillId,
    long Amount,
    HitModifiers Modifiers) : CombatEvent(Timestamp);

public sealed record HealEvent(
    DateTimeOffset Timestamp,
    ulong SourceId,
    ulong TargetId,
    uint SkillId,
    long Amount) : CombatEvent(Timestamp);

public sealed record DeathEvent(DateTimeOffset Timestamp, ulong EntityId) : CombatEvent(Timestamp);
