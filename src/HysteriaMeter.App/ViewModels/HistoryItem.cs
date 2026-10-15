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
    string VsBest = "")
{
    /// <summary>
    /// Compara mi DPS de cada pelea con mi mejor DPS contra el mismo objetivo dentro de la lista:
    /// "−12% vs récord" o "récord" en la mejor. Solo cuando hay más de una pelea contra ese objetivo.
    /// </summary>
    public static IReadOnlyList<HistoryItem> WithComparisons(IReadOnlyList<HistoryItem> items)
    {
        var best = items
            .Where(i => i.LocalDps is > 0 && i.Target != "Encuentro")
            .GroupBy(i => i.Target)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.Max(i => i.LocalDps!.Value));

        return [.. items.Select(i => i.LocalDps is { } dps && dps > 0 && best.TryGetValue(i.Target, out var top)
            ? i with { VsBest = Compare(dps, top) }
            : i)];
    }

    /// <summary>
    /// Progreso de mi DPS contra un boss (HS-05): si las peleas filtradas con mi DPS son todas contra el mismo
    /// objetivo y hay al menos dos, devuelve ese objetivo y los DPS de la más vieja a la más nueva.
    /// La lista llega de la más nueva a la más vieja, como la muestra el historial.
    /// </summary>
    public static (string Target, IReadOnlyList<double> Dps)? Progress(IEnumerable<HistoryItem> newestFirst)
    {
        var withDps = newestFirst.Where(i => i.LocalDps is > 0).ToList();
        if (withDps.Count < 2 || withDps.Select(i => i.Target).Distinct().Count() != 1 || withDps[0].Target == "Encuentro")
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

    /// <summary>Coincide con la búsqueda del panel (objetivo o nota, sin importar mayúsculas).</summary>
    public bool Matches(string? search, bool onlyKills) =>
        (!onlyKills || Killed)
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
            summary.LocalDps);
    }
}
