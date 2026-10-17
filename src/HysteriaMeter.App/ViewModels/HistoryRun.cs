using System.Globalization;
using System.Windows.Media;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Chips de filtro de la ventana del historial: se elige uno por vez, como en el mock.</summary>
public enum HistoryFilter
{
    All,
    Kills,
    Bosses,
    ThisWeek,
}

/// <summary>
/// Un grupo de la lista del historial: las peleas de una misma entrada a la zona ("Run · entrada 15/10 21:30"), o las
/// peleas seguidas que no tienen entrada registrada. El protocolo no trae el nombre de la dungeon: el título no lo inventa.
/// </summary>
/// <param name="TitleTip">Qué agrupa el título, para el tooltip.</param>
/// <param name="When">"4 peleas · 24:10": cuántas peleas tiene y cuánto duró desde la entrada.</param>
/// <param name="Summary">"2/3 bosses · 1 wipe", con la regla de bosses del modo dungeon.</param>
/// <param name="CanSummarize">Tiene entrada a la zona: se puede abrir el resumen del modo dungeon.</param>
/// <param name="First">Una pelea del grupo, para pedir ese resumen.</param>
public sealed record HistoryRun(
    string Title,
    string TitleTip,
    string When,
    string Summary,
    bool CanSummarize,
    HistoryItem First,
    IReadOnlyList<HistoryItem> Fights)
{
    /// <summary>
    /// Agrupa las peleas visibles (de la más nueva a la más vieja) en runs seguidas. El resumen y el conteo de una run
    /// salen de todas sus peleas (<paramref name="all"/>), aunque el filtro oculte alguna, como el encabezado de antes.
    /// </summary>
    public static IReadOnlyList<HistoryRun> Group(IReadOnlyList<HistoryItem> visible, IReadOnlyList<HistoryItem> all)
    {
        var groups = new List<List<HistoryItem>>();
        string? last = null;
        foreach (var item in visible)
        {
            var key = KeyOf(item);
            if (groups.Count == 0 || !string.Equals(key, last, StringComparison.Ordinal))
            {
                groups.Add([]);
            }

            groups[^1].Add(item);
            last = key;
        }

        return [.. groups.Select(g => g[0].Run is { } run ? FromRun(run, g, [.. all.Where(i => i.Run == run)]) : Loose(g))];
    }

    /// <summary>"142 peleas · 31 runs": lo que deja el filtro, no solo lo que entra en la lista.</summary>
    public static string CountText(IReadOnlyCollection<HistoryItem> matching)
    {
        var runs = matching.Select(i => i.Run).OfType<string>().Distinct(StringComparer.Ordinal).Count();
        return $"{FightsText(matching.Count)} · {runs} {(runs == 1 ? "run" : "runs")}";
    }

    /// <summary>Las peleas sin entrada a la zona van juntas mientras sean del mismo día.</summary>
    private static string KeyOf(HistoryItem item) =>
        item.Run ?? "|" + ((item.Started ?? item.Ended)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty);

    private static HistoryRun FromRun(string run, List<HistoryItem> shown, IReadOnlyList<HistoryItem> fights)
    {
        var parsed = DateTimeOffset.TryParse(run, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var entered);
        var title = parsed ? $"Run · entrada {entered.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)}" : "Run";
        var when = FightsText(fights.Count);
        if (parsed && fights.Max(f => f.Ended) is { } end && end > entered)
        {
            when += $" · {HistoryItem.FormatLength(end - entered)}";
        }

        return new HistoryRun(
            title, "Las peleas desde que entraste a la zona hasta la próxima entrada", when, BossesText(fights), CanSummarize: true, shown[0], shown);
    }

    private static HistoryRun Loose(List<HistoryItem> shown)
    {
        var day = shown[0].Started ?? shown[0].Ended;
        var title = day is { } d ? $"Peleas sueltas · {d.ToString("dd/MM", CultureInfo.InvariantCulture)}" : "Peleas sueltas";
        return new HistoryRun(
            title, "No se vio la entrada a la zona: estas peleas no se pueden juntar en una run", FightsText(shown.Count), BossesText(shown),
            CanSummarize: false, shown[0], shown);
    }

    /// <summary>"2/3 bosses · 1 wipe": bosses derrotados sobre bosses distintos intentados y cuántos wipes hubo.</summary>
    public static string BossesText(IReadOnlyCollection<HistoryItem> fights)
    {
        var bosses = fights.Where(f => !f.IsTrash).Select(f => f.Target).Distinct(StringComparer.Ordinal).Count();
        if (bosses == 0)
        {
            return "sin bosses";
        }

        var killed = fights.Where(f => f.IsKill).Select(f => f.Target).Distinct(StringComparer.Ordinal).Count();
        var wipes = fights.Count(f => f.IsWipe);
        var wipesText = wipes switch
        {
            0 => "sin wipes",
            1 => "1 wipe",
            _ => $"{wipes} wipes",
        };
        return $"{killed}/{bosses} {(bosses == 1 ? "boss" : "bosses")} · {wipesText}";
    }

    private static string FightsText(int count) => count == 1 ? "1 pelea" : $"{count} peleas";
}

/// <summary>Una barra del gráfico de progreso: alto en píxeles, color del percentil y el detalle del intento.</summary>
public sealed record ProgressBarItem(double Height, Brush Fill, string Tip);

/// <summary>
/// Columna "TU PROGRESO" del historial: mi DPS contra un boss, intento por intento, con el mejor, la mediana, las
/// kills y las muertes. Sin boss elegido, <see cref="HasData"/> es <c>false</c> y <see cref="EmptyText"/> dice por qué.
/// </summary>
/// <param name="From">Cuándo fue el intento más viejo del gráfico ("hace 3 semanas").</param>
/// <param name="To">Cuándo fue el más nuevo ("hoy").</param>
/// <param name="Kills">"9 / 14": kills sobre las peleas contra ese objetivo con mi DPS.</param>
/// <param name="Deaths">Muertes de la party sumando esas peleas.</param>
/// <param name="Summary">La línea de antes ("Tarka · 8 intentos · último… · kill más rápida 2:25"), para el tooltip.</param>
public sealed record BossProgress(
    string Title,
    string EmptyText,
    IReadOnlyList<ProgressBarItem> Bars,
    string From,
    string To,
    string Best,
    string Median,
    string Kills,
    string Deaths,
    string Summary)
{
    /// <summary>Intentos que entran en el gráfico: los más nuevos.</summary>
    public const int MaxBars = 30;

    /// <summary>Alto de la barra más alta: el área del mock (120 px) menos el margen de abajo.</summary>
    public const double ChartHeight = 112;

    public const string Header = "TU PROGRESO";

    public const string PickText = "Abrí una pelea contra un boss, o buscá uno, para ver tu DPS intento por intento.";

    public const string NoNameText = "Esta pelea no tiene el nombre del objetivo: no hay intentos con qué compararla.";

    public bool HasData => Bars.Count > 0;

    public static BossProgress Empty(string text, string title = Header) => new(title, text, [], "", "", "", "", "", "", "");

    /// <summary>
    /// El boss de la fila seleccionada; sin selección, el que deja el filtro si deja uno solo (como el progreso de antes).
    /// La serie usa todos los intentos cargados contra ese objetivo y personaje, sin que Solo kills quite los wipes.
    /// </summary>
    public static BossProgress For(IReadOnlyList<HistoryItem> matchingNewestFirst, HistoryItem? selected, DateTimeOffset now, IReadOnlyList<HistoryItem>? allNewestFirst = null)
    {
        string target;
        string? character;
        if (selected is not null)
        {
            if (selected.Target == "Encuentro")
            {
                return Empty(NoNameText);
            }

            (target, character) = (selected.Target, selected.Character);
        }
        else if (matchingNewestFirst.Where(i => i.LocalDps is > 0).ToList() is { Count: > 0 } candidates
            && candidates.Select(i => (i.Target, i.Character)).Distinct().Count() == 1 && candidates[0].Target != "Encuentro")
        {
            (target, character) = (candidates[0].Target, candidates[0].Character);
        }
        else
        {
            return Empty(PickText);
        }

        var title = $"{target.ToUpper(CultureInfo.CurrentCulture)} · {Header}";
        var series = (allNewestFirst ?? matchingNewestFirst).Where(i => i.Target == target && i.Character == character && i.LocalDps is > 0).Reverse().ToList();
        if (series.Count == 0)
        {
            return Empty($"No hay peleas tuyas contra {target} con estos filtros.", title);
        }

        var dps = series.Select(i => i.LocalDps!.Value).ToList();
        var shown = series.TakeLast(MaxBars).ToList();
        var peak = shown.Max(i => i.LocalDps!.Value);
        var bars = shown.Select(i =>
        {
            var value = i.LocalDps!.Value;
            var percentile = HistoryItem.PercentileOf(value, dps);
            var kind = i.IsKill ? " · kill" : i.IsWipe ? " · wipe" : string.Empty;
            return new ProgressBarItem(
                Math.Max(2, value / peak * ChartHeight),
                HistoryItem.TierBrush(percentile),
                $"{i.When} · {ActorRowViewModel.Compact(value)} DPS · P{percentile}{kind}");
        }).ToList();

        return new BossProgress(
            title,
            string.Empty,
            bars,
            HistoryDates.Relative(shown[0].Started ?? shown[0].Ended, now),
            HistoryDates.Relative(shown[^1].Started ?? shown[^1].Ended, now),
            ActorRowViewModel.Compact(dps.Max()),
            ActorRowViewModel.Compact(MedianOf(dps)),
            $"{series.Count(i => i.IsKill)} / {series.Count}",
            series.Sum(i => i.Deaths ?? 0).ToString(CultureInfo.CurrentCulture),
            MeterViewModel.ProgressSummary(target, dps, HistoryItem.FastestKill(series)));
    }

    public static double MedianOf(IReadOnlyCollection<double> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count == 0 ? 0 : sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}

public static class HistoryDates
{
    /// <summary>"hoy", "ayer", "hace 5 días", "hace 3 semanas" o la fecha, en días de calendario locales.</summary>
    public static string Relative(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is not { } when)
        {
            return string.Empty;
        }

        var days = (now.ToLocalTime().Date - when.ToLocalTime().Date).Days;
        return days switch
        {
            <= 0 => "hoy",
            1 => "ayer",
            < 14 => $"hace {days} días",
            < 60 => $"hace {days / 7} semanas",
            _ => when.ToLocalTime().ToString("dd/MM/yy", CultureInfo.InvariantCulture),
        };
    }
}
