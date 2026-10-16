namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Recuerda el puesto de cada jugador para marcar con ▲ o ▼ al que acaba de subir o bajar (como Details!/LOA Logs en vivo).
/// La marca dura <see cref="Duration"/> y se borra al cambiar de pelea.
/// </summary>
public sealed class RankTrends
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(3);

    private readonly Dictionary<ulong, (int Rank, string Glyph, DateTimeOffset At)> _byActor = [];
    private (Guid Encounter, int View) _current;

    /// <summary>Empieza de cero si la pelea es otra o cambió lo que se ordena (pestaña, solo boss).</summary>
    public void Begin(Guid encounter, int view)
    {
        if ((encounter, view) != _current)
        {
            _byActor.Clear();
            _current = (encounter, view);
        }
    }

    /// <summary>"▲" si subió hace poco, "▼" si bajó y vacío si sigue igual (o es la primera vez que aparece).</summary>
    public string Update(ulong actor, int rank, DateTimeOffset now)
    {
        if (!_byActor.TryGetValue(actor, out var last))
        {
            _byActor[actor] = (rank, string.Empty, now);
            return string.Empty;
        }

        if (rank != last.Rank)
        {
            var glyph = rank < last.Rank ? "▲" : "▼";
            _byActor[actor] = (rank, glyph, now);
            return glyph;
        }

        return now - last.At < Duration ? last.Glyph : string.Empty;
    }
}
