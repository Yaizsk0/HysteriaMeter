using System.Globalization;
using HysteriaMeter.Storage;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Un encuentro guardado, como se lista en el panel de historial.</summary>
public sealed record HistoryItem(string Id, string Target, string When, string Duration, string Total, bool Killed)
{
    public string Result => Killed ? "· derrotado" : string.Empty;

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
            summary.TargetKilled);
    }
}
