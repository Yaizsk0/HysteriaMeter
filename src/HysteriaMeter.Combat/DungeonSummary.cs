namespace HysteriaMeter.Combat;

/// <summary>Cómo distinguir un boss de los bichos normales, con lo que trae una pelea guardada.</summary>
public static class BossRules
{
    /// <summary>Peleas de al menos este largo cuentan como boss aunque no estén marcadas en data/npcs.json (el trash muere antes).</summary>
    public static readonly TimeSpan BossKillMinDuration = TimeSpan.FromSeconds(30);

    /// <summary>Parte del daño que tiene que ir al objetivo principal para tomar la pelea como de boss.</summary>
    public const double BossFightShare = 0.6;

    /// <summary>
    /// Pelea contra un boss: marcado en data/npcs.json, o un objetivo que se llevó la mayor parte del daño.
    /// En un pull de trash el daño se reparte entre varios bichos.
    /// </summary>
    public static bool LooksLikeBossFight(EncounterSnapshot snapshot) =>
        LooksLikeBossFight(snapshot.TargetIsBoss, snapshot.Actors.Sum(a => a.Damage), snapshot.Actors.Sum(a => a.MainTargetDamage));

    /// <summary>La misma regla con las sumas de daño de la party, como las trae una pelea guardada sin cargarla entera.</summary>
    public static bool LooksLikeBossFight(bool targetIsBoss, long actorsDamage, long mainTargetDamage) =>
        targetIsBoss || (actorsDamage > 0 && mainTargetDamage >= actorsDamage * BossFightShare);

    /// <summary>Un boss derrotado: marcado como boss en data/npcs.json, o un objetivo que aguantó al menos 30 s.</summary>
    public static bool IsBossKill(EncounterSnapshot snapshot) =>
        IsBossKill(snapshot.TargetKilled, snapshot.TargetIsBoss, snapshot.Duration, snapshot.MainTargetDuration);

    public static bool IsBossKill(bool killed, bool targetIsBoss, TimeSpan duration, TimeSpan? mainTargetDuration) =>
        killed && (targetIsBoss || (mainTargetDuration ?? duration) >= BossKillMinDuration);

    /// <summary>
    /// Intento contra un boss (kill o wipe). Un objetivo que no murió cuenta si se llevó la mayor parte del daño
    /// y la pelea duró lo que dura un boss; si no, es trash.
    /// </summary>
    public static bool IsBossAttempt(EncounterSnapshot snapshot) =>
        IsBossAttempt(
            snapshot.TargetKilled, snapshot.TargetName is not null, snapshot.TargetIsBoss, snapshot.Duration, snapshot.MainTargetDuration,
            snapshot.Actors.Sum(a => a.Damage), snapshot.Actors.Sum(a => a.MainTargetDamage));

    /// <summary>La misma regla con los datos sueltos de una pelea guardada (historial).</summary>
    public static bool IsBossAttempt(
        bool killed, bool hasTargetName, bool targetIsBoss, TimeSpan duration, TimeSpan? mainTargetDuration, long actorsDamage, long mainTargetDamage) =>
        IsBossKill(killed, targetIsBoss, duration, mainTargetDuration)
        || (!killed && hasTargetName && LooksLikeBossFight(targetIsBoss, actorsDamage, mainTargetDamage)
            && (targetIsBoss || duration >= BossKillMinDuration));

    /// <summary>Trash, kill o wipe, como la línea de tiempo del modo dungeon y las insignias del historial.</summary>
    public static RunSegmentKind Classify(bool isBossAttempt, bool killed) =>
        !isBossAttempt ? RunSegmentKind.Trash : killed ? RunSegmentKind.Kill : RunSegmentKind.Wipe;
}

public enum RunSegmentKind
{
    Trash,
    Kill,
    Wipe,
}

/// <summary>Un tramo de la línea de tiempo de la run, en segundos desde la entrada.</summary>
public sealed record RunSegment(RunSegmentKind Kind, string Label, double StartSecond, double Seconds);

/// <summary>Un jugador en un boss: DPS del intento que cuenta (el kill, o el último) y muertes sumando todos los intentos.</summary>
public sealed record BossPlayerLine(string Name, int? ClassId, bool IsLocalPlayer, double Dps, int Deaths);

/// <summary>Un boss de la run con todos sus intentos.</summary>
public sealed record BossSummary(
    string Name,
    int Attempts,
    bool Killed,
    TimeSpan Duration,
    double PartyDps,
    int Deaths,
    IReadOnlyList<BossPlayerLine> Players)
{
    public int Wipes => Killed ? Attempts - 1 : Attempts;
}

/// <summary>Un jugador en toda la run: DPS sobre el tiempo de combate y muertes en todas las peleas.</summary>
public sealed record RunPlayerLine(string Name, int? ClassId, bool IsLocalPlayer, double Dps, int Deaths);

/// <summary>Resumen de una run de dungeon: desde la entrada a la zona hasta la última pelea.</summary>
public sealed record DungeonSummary(
    DateTimeOffset Entered,
    DateTimeOffset Left,
    IReadOnlyList<BossSummary> Bosses,
    IReadOnlyList<RunPlayerLine> Players,
    IReadOnlyList<RunSegment> Timeline)
{
    public TimeSpan Duration => Left - Entered;

    public int BossesKilled => Bosses.Count(b => b.Killed);

    public int Wipes => Bosses.Sum(b => b.Wipes);

    public int Deaths => Players.Sum(p => p.Deaths);

    /// <summary>
    /// Arma el resumen con las peleas de una misma entrada. Los intentos se agrupan por boss (plantilla de NPC, o nombre)
    /// en el orden en que aparecieron.
    /// </summary>
    public static DungeonSummary From(IEnumerable<EncounterSnapshot> fights, DateTimeOffset? entered = null)
    {
        var ordered = fights.OrderBy(f => f.Start).ToList();
        if (ordered.Count == 0)
        {
            var now = entered ?? DateTimeOffset.MinValue;
            return new DungeonSummary(now, now, [], [], []);
        }

        var start = entered is { } e && e <= ordered[0].Start ? e : ordered[0].Start;
        var end = ordered.Max(f => f.End);

        var bosses = ordered.Where(BossRules.IsBossAttempt)
            .GroupBy(f => f.TargetNpcId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? f.TargetName!, StringComparer.Ordinal)
            .Select(Boss)
            .ToList();

        var timeline = ordered.Select(f =>
        {
            var kind = BossRules.Classify(BossRules.IsBossAttempt(f), f.TargetKilled);
            var label = kind == RunSegmentKind.Trash ? "Trash" : f.TargetName ?? "Boss";
            return new RunSegment(kind, label, (f.Start - start).TotalSeconds, f.Duration.TotalSeconds);
        }).ToList();

        var players = ordered.SelectMany(f => f.Actors.Select(a => (Fight: f, Actor: a)))
            .GroupBy(x => x.Actor.Name, StringComparer.Ordinal)
            .Select(g =>
            {
                var seconds = g.Sum(x => x.Fight.Duration.TotalSeconds);
                return new RunPlayerLine(
                    g.Key,
                    g.Select(x => x.Actor.ClassId).LastOrDefault(c => c is not null),
                    g.Any(x => x.Actor.IsLocalPlayer),
                    seconds > 0 ? g.Sum(x => x.Actor.Damage) / seconds : 0,
                    g.Sum(x => x.Actor.Deaths));
            })
            .OrderByDescending(p => p.Dps)
            .ToList();

        return new DungeonSummary(start, end, bosses, players, timeline);
    }

    private static BossSummary Boss(IEnumerable<EncounterSnapshot> attempts)
    {
        var list = attempts.ToList();
        var deciding = list.LastOrDefault(f => f.TargetKilled) ?? list[^1];
        var duration = deciding.MainTargetDuration ?? deciding.Duration;
        var deaths = list.SelectMany(f => f.Actors).GroupBy(a => a.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Deaths), StringComparer.Ordinal);
        var players = deciding.Actors
            .Select(a => new BossPlayerLine(a.Name, a.ClassId, a.IsLocalPlayer, a.MainTargetDps > 0 ? a.MainTargetDps : a.Dps, deaths.GetValueOrDefault(a.Name)))
            .Concat(deaths.Keys.Where(n => deciding.Actors.All(a => a.Name != n))
                .Select(n => list.SelectMany(f => f.Actors).Last(a => a.Name == n))
                .Select(a => new BossPlayerLine(a.Name, a.ClassId, a.IsLocalPlayer, 0, deaths[a.Name])))
            .OrderByDescending(p => p.Dps)
            .ToList();
        var partyDamage = deciding.Actors.Sum(a => a.MainTargetDamage > 0 ? a.MainTargetDamage : a.Damage);
        return new BossSummary(
            deciding.TargetName ?? "Boss",
            list.Count,
            deciding.TargetKilled,
            duration,
            partyDamage / Math.Max(duration.TotalSeconds, 1),
            deaths.Values.Sum(),
            players);
    }
}
