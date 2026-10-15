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
    TimeSpan? MainTargetDuration = null)
{
    /// <summary>Duración mínima de 1 s para que un único golpe no dé un DPS absurdo.</summary>
    public TimeSpan Duration => TimeSpan.FromTicks(Math.Max((End - Start).Ticks, TimeSpan.TicksPerSecond));
}

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
    double MainTargetDps = 0)
{
    public double CritRate => Hits == 0 ? 0 : (double)Crits / Hits;
}

public sealed record SkillSnapshot(uint SkillId, long Damage, int Hits, int Crits, long MaxHit, double Share, HitCounts? Counts = null);

/// <summary>
/// Golpes por modificador, como las columnas de Combat Analysis (CRI, PERF, DOUB, FRNT, BACK, MULT).
/// Un modificador fuera de <see cref="HysteriaMeter.Protocol.Events.ModifierCoverage"/> se muestra como "sin identificar".
/// </summary>
public sealed record HitCounts(int Hits, int Critical, int Perfect, int DoubleHit, int Front, int Back, int MultiHit)
{
    public static HitCounts Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    public double Rate(int count) => Hits == 0 ? 0 : (double)count / Hits;
}
