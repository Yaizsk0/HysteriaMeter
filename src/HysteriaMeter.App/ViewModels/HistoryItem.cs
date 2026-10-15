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
    bool IsRecord = false)
{
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
            summary.IsRecord && summary.Attempt > 1);
    }
}
