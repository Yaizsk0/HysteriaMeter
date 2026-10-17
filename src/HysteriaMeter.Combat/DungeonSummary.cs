using System.Globalization;

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
    /// Intento contra un boss (kill o wipe). Un objetivo que no murió cuenta si tiene nombre o plantilla de NPC, se llevó
    /// la mayor parte del daño y la pelea duró lo que dura un boss; si no, es trash.
    /// </summary>
    public static bool IsBossAttempt(EncounterSnapshot snapshot) =>
        IsBossAttempt(
            snapshot.TargetKilled, FightTarget.Key(snapshot) is not null, snapshot.TargetIsBoss, snapshot.Duration, snapshot.MainTargetDuration,
            snapshot.Actors.Sum(a => a.Damage), snapshot.Actors.Sum(a => a.MainTargetDamage));

    /// <summary>
    /// La misma regla con los datos sueltos de una pelea guardada (historial). <paramref name="hasTarget"/>: el objetivo
    /// tiene nombre o plantilla de NPC (<see cref="FightTarget.Key(string?, uint?)"/>). Sin ninguno de los dos, un wipe
    /// no se distingue de un pull de trash y cuenta como trash.
    /// </summary>
    public static bool IsBossAttempt(
        bool killed, bool hasTarget, bool targetIsBoss, TimeSpan duration, TimeSpan? mainTargetDuration, long actorsDamage, long mainTargetDamage) =>
        IsBossKill(killed, targetIsBoss, duration, mainTargetDuration)
        || (!killed && hasTarget && LooksLikeBossFight(targetIsBoss, actorsDamage, mainTargetDamage)
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

/// <summary>
/// Un tramo de la línea de tiempo de la run, en segundos desde la entrada. <paramref name="Label"/> es "Trash" o el nombre
/// del boss como en su tarjeta; <paramref name="FightId"/> es la pelea guardada de ese tramo.
/// </summary>
public sealed record RunSegment(RunSegmentKind Kind, string Label, double StartSecond, double Seconds, Guid FightId = default);

/// <summary>Un jugador en un boss: DPS del intento que cuenta (el kill, o el último) y muertes sumando todos los intentos.</summary>
public sealed record BossPlayerLine(string Name, int? ClassId, bool IsLocalPlayer, double Dps, int Deaths);

/// <summary>
/// Un intento contra un boss que no terminó en kill: cuánto duró y la fase más baja que se vio
/// (<paramref name="BelowPercent"/>: la vida bajó del 75, 50 o 25 %; <c>null</c> si no bajó del 75 % o no se midió).
/// </summary>
public sealed record BossWipe(Guid FightId, TimeSpan Duration, int? BelowPercent);

/// <summary>
/// Un boss de la run con todos sus intentos. <paramref name="DecidingId"/> es el intento que cuenta (el kill, o el último),
/// el que dan el DPS y la duración. <paramref name="WipeList"/> son los intentos sin kill: solo esos son wipes.
/// </summary>
public sealed record BossSummary(
    string Name,
    int Attempts,
    bool Killed,
    TimeSpan Duration,
    double PartyDps,
    int Deaths,
    IReadOnlyList<BossPlayerLine> Players,
    Guid DecidingId,
    IReadOnlyList<BossWipe> WipeList)
{
    public int Wipes => WipeList.Count;
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
    /// Arma el resumen con las peleas de una misma entrada. Los intentos se agrupan por boss (plantilla de NPC o, sin
    /// plantilla, nombre) en el orden en que aparecieron; un kill cierra el boss y otra pelea contra la misma plantilla
    /// después es otro boss. Una pelea sin plantilla ni nombre (guardada antes de registrar la plantilla) no se junta con
    /// ninguna: es su propio boss si fue kill, y trash si no (no hay cómo saber que fue un wipe).
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

        var groups = new List<List<EncounterSnapshot>>();
        var open = new Dictionary<string, List<EncounterSnapshot>>(StringComparer.Ordinal);
        foreach (var fight in ordered.Where(BossRules.IsBossAttempt))
        {
            var key = GroupKey(fight);
            if (!open.TryGetValue(key, out var attempts))
            {
                attempts = [];
                open[key] = attempts;
                groups.Add(attempts);
            }

            attempts.Add(fight);
            if (fight.TargetKilled)
            {
                open.Remove(key);
            }
        }

        var names = BossNames(groups);
        var bosses = groups.Select((g, i) => Boss(names[i], g)).ToList();
        var bossOf = groups.SelectMany((g, i) => g.Select(f => (f.Id, Name: names[i]))).ToDictionary(x => x.Id, x => x.Name);

        var timeline = ordered.Select(f =>
        {
            var kind = BossRules.Classify(bossOf.ContainsKey(f.Id), f.TargetKilled);
            var label = bossOf.GetValueOrDefault(f.Id) ?? "Trash";
            return new RunSegment(kind, label, (f.Start - start).TotalSeconds, f.Duration.TotalSeconds, f.Id);
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

    /// <summary>La plantilla primero (dos plantillas con el mismo nombre son dos bosses); sin plantilla ni nombre, la pelea sola.</summary>
    private static string GroupKey(EncounterSnapshot fight) =>
        fight.TargetNpcId is { } npc ? "npc:" + npc.ToString(CultureInfo.InvariantCulture)
        : FightTarget.Name(fight.TargetName) is { } name ? "nombre:" + name
        : "pelea:" + fight.Id.ToString("N");

    /// <summary>
    /// El nombre de cada boss como en el overlay ("Kromede", "NPC 2400123"); sin nombre ni plantilla, "Boss N" por su
    /// lugar en la run. Si el mismo nombre vuelve (otra plantilla, u otra vez después del kill), "Kromede (2)".
    /// </summary>
    private static List<string> BossNames(List<List<EncounterSnapshot>> groups)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = new List<string>(groups.Count);
        for (var i = 0; i < groups.Count; i++)
        {
            var name = FightTarget.Key(Deciding(groups[i])) ?? $"Boss {(i + 1).ToString(CultureInfo.InvariantCulture)}";
            var count = seen.GetValueOrDefault(name) + 1;
            seen[name] = count;
            names.Add(count == 1 ? name : $"{name} ({count.ToString(CultureInfo.InvariantCulture)})");
        }

        return names;
    }

    private static EncounterSnapshot Deciding(List<EncounterSnapshot> attempts) => attempts.LastOrDefault(f => f.TargetKilled) ?? attempts[^1];

    private static BossSummary Boss(string name, List<EncounterSnapshot> list)
    {
        var deciding = Deciding(list);
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
        var wipes = list.Where(f => !f.TargetKilled)
            .Select(f => new BossWipe(f.Id, f.MainTargetDuration ?? f.Duration, f.Phases is { Count: > 0 } phases ? phases.Min(p => p.Percent) : null))
            .ToList();
        return new BossSummary(
            name,
            list.Count,
            deciding.TargetKilled,
            duration,
            partyDamage / Math.Max(duration.TotalSeconds, 1),
            deaths.Values.Sum(),
            players,
            deciding.Id,
            wipes);
    }
}
