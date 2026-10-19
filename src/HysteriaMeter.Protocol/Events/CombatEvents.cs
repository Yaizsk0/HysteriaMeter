namespace HysteriaMeter.Protocol.Events;

/// <summary>Evento de dominio producido al decodificar un paquete. Los IDs son los crudos del servidor.</summary>
public abstract record CombatEvent(DateTimeOffset Timestamp);

/// <summary>El servidor identificó a nuestro personaje.</summary>
public sealed record LocalPlayerEvent(DateTimeOffset Timestamp, ulong EntityId, string? Name) : CombatEvent(Timestamp);

/// <summary>
/// Aparece una entidad. <paramref name="OwnerId"/> es el id de entidad del dueño de una invocación, leído de la cola
/// del 0x3641 cuando tiene la forma conocida. Algunas traen además, o en su lugar, el nombre del dueño
/// (<paramref name="OwnerName"/>); el motor lo resuelve contra los nombres de jugadores que conoce.
/// </summary>
public sealed record EntitySpawnEvent(DateTimeOffset Timestamp, ulong EntityId, ulong? OwnerId, uint? NpcId, string? OwnerName = null, long? Hp = null, long? MaxHp = null) : CombatEvent(Timestamp);

/// <summary>Vida actual de una entidad (el servidor la manda después de cada cambio).</summary>
public sealed record HealthEvent(DateTimeOffset Timestamp, ulong EntityId, long Hp) : CombatEvent(Timestamp);

/// <summary>
/// Llegó un dato de un miembro de la party (HP/MP, cerca de una vez por segundo). El servidor solo los manda
/// para compañeros de party, nunca para jugadores random ni para el local.
/// </summary>
public sealed record PartyMemberSeenEvent(DateTimeOffset Timestamp, ulong EntityId) : CombatEvent(Timestamp);

/// <summary>Nombre / clase de un jugador. Puede llegar tarde o no llegar.</summary>
public sealed record EntityInfoEvent(DateTimeOffset Timestamp, ulong EntityId, string Name, int? ClassId) : CombatEvent(Timestamp);

/// <summary>
/// Equipo de un jugador: item level y power (CP). Cada dato puede faltar: no todas las apariciones lo traen.
/// </summary>
public sealed record PlayerGearEvent(DateTimeOffset Timestamp, ulong EntityId, int? ItemLevel, long? CombatPower) : CombatEvent(Timestamp);

/// <summary>Power (CP) del jugador local; el servidor lo manda al entrar a la zona y cuando cambia.</summary>
public sealed record LocalCombatPowerEvent(DateTimeOffset Timestamp, long CombatPower) : CombatEvent(Timestamp);

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
    FrontAttack = 1 << 6,
    MultiHit = 1 << 7,
    Perfect = 1 << 8,
    DoubleHit = 1 << 9,
}

/// <summary>
/// Qué modificadores sabemos leer del protocolo. Un modificador fuera de <see cref="Identified"/> se
/// muestra como "sin identificar" en vez de 0 %.
/// </summary>
public static class ModifierCoverage
{
    public const HitModifiers Identified =
        HitModifiers.Critical | HitModifiers.BackAttack | HitModifiers.FrontAttack | HitModifiers.MultiHit
        | HitModifiers.Perfect | HitModifiers.DoubleHit | HitModifiers.DamageOverTime;

    public static bool IsIdentified(HitModifiers modifier) => (Identified & modifier) == modifier;
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
