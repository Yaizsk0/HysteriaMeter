namespace HysteriaMeter.Combat;

/// <summary>Foto inmutable de un encuentro, lista para la UI o para persistir.</summary>
public sealed record EncounterSnapshot(
    Guid Id,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? TargetName,
    bool TargetKilled,
    long TotalDamage,
    IReadOnlyList<ActorSnapshot> Actors)
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
    IReadOnlyList<SkillSnapshot> Skills)
{
    public double CritRate => Hits == 0 ? 0 : (double)Crits / Hits;
}

public sealed record SkillSnapshot(uint SkillId, long Damage, int Hits, int Crits, long MaxHit, double Share);
