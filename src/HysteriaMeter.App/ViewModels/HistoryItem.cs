using System.Globalization;
using HysteriaMeter.Storage;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Un encuentro guardado, como se lista en el panel de historial.</summary>
public sealed record HistoryItem(
    string Id,
    string Target,
    string When,
    string Duration,
    string Total,
    bool Killed,
    string? Note = null,
    string MyDps = "",
    int Attempt = 0,
    bool IsRecord = false,
    double? LocalDps = null,
    string VsBest = "",
    // Entrada a la zona (ISO 8601) y fin de la pelea: las peleas con la misma entrada son una "run".
    string? Run = null,
    DateTimeOffset? Ended = null,
    string RunHeader = "",
    int? Percentile = null,
    // Personaje local de esa pelea: récords, intentos y percentiles se cuentan por personaje.
    string? Character = null,
    string CharacterTag = "",
    // Largo de la pelea en segundos, para la kill más rápida.
    double? Seconds = null,
    string FastestTag = "")
{
    /// <summary>La kill más rápida de las peleas que muestra el historial (todas contra el mismo boss), o <c>null</c>.</summary>
    public static TimeSpan? FastestKill(IEnumerable<HistoryItem> items) =>
        items.Where(i => i.Killed && i.Seconds is > 0).Select(i => i.Seconds!.Value).DefaultIfEmpty().Min() is var best and > 0
            ? TimeSpan.FromSeconds(best)
            : null;

    /// <summary>Opción del filtro de personaje que muestra todos.</summary>
    public const string AllCharacters = "Todos";

    /// <summary>Con más de un personaje en el historial, cada fila dice con cuál fue ("· Griffith").</summary>
    public static IReadOnlyList<HistoryItem> WithCharacterTags(IReadOnlyList<HistoryItem> items) =>
        items.Select(i => i.Character).Where(c => c is not null).Distinct(StringComparer.Ordinal).Count() > 1
            ? [.. items.Select(i => i.Character is { } c ? i with { CharacterTag = $"· {c}" } : i)]
            : items;

    /// <summary>"P87": qué parte de tus intentos contra ese objetivo hiciste igual o menos DPS que en este.</summary>
    public string PercentileText => Percentile is { } p ? $"P{p}" : string.Empty;

    public bool HasPercentile => Percentile is not null;

    /// <summary>Color del percentil, como en Warcraft Logs: gris, verde, azul, violeta, naranja y dorado el mejor.</summary>
    public System.Windows.Media.Brush PercentileBrush => TierBrush(Percentile ?? 0);

    public static System.Windows.Media.Brush TierBrush(int percentile) => Tiers[percentile switch
    {
        >= 100 => 5,
        >= 95 => 4,
        >= 75 => 3,
        >= 50 => 2,
        >= 25 => 1,
        _ => 0,
    }];

    private static readonly System.Windows.Media.Brush[] Tiers =
        [.. new[] { "#9D9D9D", "#1EFF00", "#0070FF", "#A335EE", "#FF8000", "#E5CC80" }.Select(Freeze)];

    private static System.Windows.Media.Brush Freeze(string hex)
    {
        var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>Percentil de <paramref name="dps"/> entre <paramref name="all"/>: el mejor da 100.</summary>
    public static int PercentileOf(double dps, IReadOnlyCollection<double> all) =>
        all.Count == 0 ? 0 : (int)Math.Round(100.0 * all.Count(d => d <= dps) / all.Count);

    public bool HasRunHeader => RunHeader.Length > 0;

    /// <summary>
    /// Agrupa por entrada a dungeon: la primera pelea visible de cada run con 2 o más peleas lleva el encabezado
    /// "ENTRADA 21:30 · 4 peleas · 2 bosses · 24:10", contado sobre todas las peleas de esa run (aunque el filtro oculte alguna).
    /// </summary>
    public static IReadOnlyList<HistoryItem> WithRuns(IReadOnlyList<HistoryItem> visible, IReadOnlyList<HistoryItem> all)
    {
        var runs = all.Where(i => i.Run is not null).GroupBy(i => i.Run!, StringComparer.Ordinal)
            .Where(g => g.Count() >= 2)
            .ToDictionary(g => g.Key, g => RunText(g.Key, [.. g]), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return [.. visible.Select(i => i with
        {
            RunHeader = i.Run is { } run && runs.TryGetValue(run, out var header) && seen.Add(run) ? header : string.Empty,
        })];
    }

    private static string RunText(string run, IReadOnlyList<HistoryItem> fights)
    {
        if (!DateTimeOffset.TryParse(run, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var entered))
        {
            return string.Empty;
        }

        var bosses = fights.Count(f => f.Killed);
        var text = $"ENTRADA {entered.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)} · {fights.Count} peleas";
        if (bosses > 0)
        {
            text += bosses == 1 ? " · 1 boss" : $" · {bosses} bosses";
        }

        if (fights.Max(f => f.Ended) is { } last && last > entered)
        {
            text += $" · {FormatLength(last - entered)}";
        }

        return text;
    }

    private static string FormatLength(TimeSpan length) =>
        length.TotalHours >= 1
            ? $"{(int)length.TotalHours}:{length.Minutes:00}:{length.Seconds:00}"
            : length.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Compara mi DPS de cada pelea con mi mejor DPS contra el mismo objetivo dentro de la lista:
    /// "−12% vs récord" o "récord" en la mejor. Solo cuando hay más de una pelea contra ese objetivo.
    /// </summary>
    public static IReadOnlyList<HistoryItem> WithComparisons(IReadOnlyList<HistoryItem> items)
    {
        var attempts = items
            .Where(i => i.LocalDps is > 0 && i.Target != "Encuentro")
            .GroupBy(i => (i.Target, i.Character))
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<double>)[.. g.Select(i => i.LocalDps!.Value)]);

        // La kill más rápida contra cada boss (por personaje), si hubo más de una kill.
        var fastest = items
            .Where(i => i.Killed && i.Seconds is > 0 && i.Target != "Encuentro")
            .GroupBy(i => (i.Target, i.Character))
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.MinBy(i => i.Seconds)!.Id);

        return [.. items.Select(i => i.LocalDps is { } dps && dps > 0 && attempts.TryGetValue((i.Target, i.Character), out var all)
            ? i with { VsBest = Compare(dps, all.Max()), Percentile = PercentileOf(dps, all) }
            : i)
            .Select(i => fastest.TryGetValue((i.Target, i.Character), out var id) && id == i.Id ? i with { FastestTag = "· ⏱ kill más rápida" } : i)];
    }

    /// <summary>
    /// Progreso de mi DPS contra un boss (HS-05): si las peleas filtradas con mi DPS son todas contra el mismo
    /// objetivo y hay al menos dos, devuelve ese objetivo y los DPS de la más vieja a la más nueva.
    /// La lista llega de la más nueva a la más vieja, como la muestra el historial.
    /// </summary>
    public static (string Target, IReadOnlyList<double> Dps)? Progress(IEnumerable<HistoryItem> newestFirst)
    {
        var withDps = newestFirst.Where(i => i.LocalDps is > 0).ToList();
        if (withDps.Count < 2 || withDps.Select(i => (i.Target, i.Character)).Distinct().Count() != 1 || withDps[0].Target == "Encuentro")
        {
            return null;
        }

        return (withDps[0].Target, [.. withDps.Select(i => i.LocalDps!.Value).Reverse()]);
    }

    /// <summary>"récord" si es el mejor; si no, cuánto le faltó en porcentaje.</summary>
    public static string Compare(double dps, double best) =>
        dps >= best ? "· mejor intento" : $"· {((dps / best) - 1).ToString("+0%;−0%", CultureInfo.CurrentCulture)} vs mejor";

    public string Result => Killed ? "· derrotado" : string.Empty;

    /// <summary>"intento 3" desde la segunda vez contra el mismo objetivo.</summary>
    public string AttemptText => Attempt > 1 ? $"· intento {Attempt}" : string.Empty;

    public bool HasNote => !string.IsNullOrEmpty(Note);

    /// <summary>Coincide con la búsqueda del panel (objetivo o nota, sin importar mayúsculas) y con el personaje elegido.</summary>
    public bool Matches(string? search, bool onlyKills, string? character = null) =>
        (!onlyKills || Killed)
        && (character is null or AllCharacters || Character == character)
        && (string.IsNullOrWhiteSpace(search)
            || Target.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase)
            || (Note?.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase) ?? false));

    public static HistoryItem From(EncounterSummary summary)
    {
        var start = DateTimeOffset.Parse(summary.StartedAt, CultureInfo.InvariantCulture).ToLocalTime();
        var end = DateTimeOffset.Parse(summary.EndedAt, CultureInfo.InvariantCulture).ToLocalTime();
        return new HistoryItem(
            summary.Id,
            // Las peleas guardadas antes de este cambio pueden tener "#entidad" como nombre.
            summary.TargetName is { Length: > 0 } name && name[0] != '#' ? name : "Encuentro",
            start.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture),
            (end - start).ToString(@"mm\:ss", CultureInfo.InvariantCulture),
            ActorRowViewModel.Compact(summary.TotalDamage),
            summary.TargetKilled,
            summary.Note,
            summary.LocalDps is { } dps ? $"mi DPS {ActorRowViewModel.Compact(dps)}" : string.Empty,
            summary.Attempt,
            summary.IsRecord && summary.Attempt > 1,
            summary.LocalDps,
            Run: summary.ZoneEnteredAt,
            Ended: end,
            Character: summary.LocalName,
            Seconds: (end - start).TotalSeconds);
    }
}
