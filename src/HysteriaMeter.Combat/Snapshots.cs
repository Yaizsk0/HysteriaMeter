namespace HysteriaMeter.Combat;

/// <summary>Foto inmutable de un encuentro, lista para la UI o para persistir.</summary>
public sealed record EncounterSnapshot(
    Guid Id,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? TargetName,
    bool TargetKilled,
    long TotalDamage,
    IReadOnlyList<ActorSnapshot> Actors,
    TimeSpan? MainTargetDuration = null,
    // Vida del objetivo principal, si la conocemos (spawn 0x3641 y actualizaciones 0x8D00).
    Health? TargetHealth = null,
    // Muertes de jugadores durante la pelea, con los últimos golpes recibidos.
    IReadOnlyList<DeathRecap>? Deaths = null,
    // ID de plantilla del objetivo principal (para ponerle nombre en data/npcs.json) y si la tabla lo marca como boss.
    uint? TargetNpcId = null,
    bool TargetIsBoss = false,
    // Momentos marcados por el jugador con el atajo, en segundos desde el inicio.
    IReadOnlyList<int>? Marks = null,
    // Entrada a la zona donde empezó la pelea: las peleas con la misma entrada son una "run" de dungeon.
    DateTimeOffset? ZoneEnteredAt = null,
    // Segundos hasta el enrage del objetivo principal, si data/npcs.json lo trae.
    int? TargetEnrageSeconds = null,
    // Cuándo la vida del objetivo principal bajó del 75, 50 y 25 % (fases de la pelea).
    IReadOnlyList<HealthPhase>? Phases = null)
{
    /// <summary>Duración mínima de 1 s para que un único golpe no dé un DPS absurdo.</summary>
    public TimeSpan Duration => TimeSpan.FromTicks(Math.Max((End - Start).Ticks, TimeSpan.TicksPerSecond));
}

/// <summary>La vida del objetivo bajó de <paramref name="Percent"/> % a los <paramref name="Second"/> segundos de pelea.</summary>
public sealed record HealthPhase(int Percent, int Second);

public sealed record ActorSnapshot(
    ulong Id,
    string Name,
    int? ClassId,
    bool IsLocalPlayer,
    long Damage,
    double Dps,
    double Share,
    int Hits,
    int Crits,
    long MaxHit,
    long Healing,
    IReadOnlyList<SkillSnapshot> Skills,
    // Golpes por modificador contra el objetivo principal, como Combat Analysis.
    HitCounts? Counts = null,
    long MainTargetDamage = 0,
    double MainTargetDps = 0,
    long PeriodicDamage = 0,
    // DPS sin tiempo muerto: los huecos de más de 3 s entre golpes cuentan como 3 s.
    double ActiveDps = 0,
    long Overkill = 0,
    // Daño por segundo desde el inicio del encuentro, para el gráfico de DPS en el tiempo.
    IReadOnlyList<long>? Timeline = null,
    long DamageTaken = 0,
    int HitsTaken = 0,
    int Deaths = 0,
    // Item level y power (CP) del jugador, si llegaron en su aparición.
    int? ItemLevel = null,
    long? CombatPower = null)
{
    public double CritRate => Hits == 0 ? 0 : (double)Crits / Hits;
}

public sealed record SkillSnapshot(uint SkillId, long Damage, int Hits, int Crits, long MaxHit, double Share, HitCounts? Counts = null, long PeriodicDamage = 0)
{
    public long DirectDamage => Damage - PeriodicDamage;
}

/// <summary>
/// Golpes por modificador, como las columnas de Combat Analysis (CRI, PERF, DOUB, FRNT, BACK, MULT).
/// Un modificador fuera de <see cref="HysteriaMeter.Protocol.Events.ModifierCoverage"/> se muestra como "sin identificar".
/// </summary>
public sealed record HitCounts(int Hits, int Critical, int Perfect, int DoubleHit, int Front, int Back, int MultiHit)
{
    public static HitCounts Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    public double Rate(int count) => Hits == 0 ? 0 : (double)count / Hits;
}
