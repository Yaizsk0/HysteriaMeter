using System.Globalization;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Un encuentro guardado, como se lista en la ventana del historial.</summary>
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
    int? Percentile = null,
    // Personaje local de esa pelea: récords, intentos y percentiles se cuentan por personaje.
    string? Character = null,
    string CharacterTag = "",
    // Largo de la pelea en segundos, para la kill más rápida.
    double? Seconds = null,
    string FastestTag = "",
    // Inicio de la pelea (hora local): "Esta semana", la fecha de las peleas sin run y el gráfico de progreso.
    DateTimeOffset? Started = null,
    // Trash, kill o wipe con la regla del modo dungeon (BossRules), para la insignia y "Solo bosses".
    RunSegmentKind Kind = RunSegmentKind.Trash,
    // Muertes de toda la party en la pelea; null si no tiene jugadores guardados.
    int? Deaths = null,
    // "+8%": mi DPS contra el intento anterior contra ese objetivo, solo en las kills.
    string Delta = "",
    bool DeltaPositive = false)
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

    /// <summary>
    /// Color del percentil, con los umbrales de Warcraft Logs y los colores del rediseño: gris, verde, azul, violeta,
    /// naranja y el acento el mejor. Son los Percentile* de Themes/Theme.xaml: mantener los dos iguales.
    /// </summary>
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
        [.. new[] { "#9D9D9D", "#4FD15C", "#4A90E8", "#B867F0", "#F0A04A", "#E8B04A" }.Select(Freeze)];

    private static System.Windows.Media.Brush Freeze(string hex)
    {
        var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>Percentil de <paramref name="dps"/> entre <paramref name="all"/>: el mejor da 100.</summary>
    public static int PercentileOf(double dps, IReadOnlyCollection<double> all) =>
        all.Count == 0 ? 0 : (int)Math.Round(100.0 * all.Count(d => d <= dps) / all.Count);

    /// <summary>"24:10" o "1:02:05": cuánto duró una run desde la entrada a la zona.</summary>
    internal static string FormatLength(TimeSpan length) =>
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

    /// <summary>
    /// Coincide con la búsqueda, el chip de filtro y el personaje. "Solo kills" son los bosses derrotados (✓) y
    /// "Esta semana", los últimos 7 días hasta <paramref name="now"/>.
    /// </summary>
    public bool Matches(string? search, HistoryFilter filter, string? character, DateTimeOffset now) =>
        Matches(search, onlyKills: false, character)
        && filter switch
        {
            HistoryFilter.Kills => IsKill,
            HistoryFilter.Bosses => !IsTrash,
            HistoryFilter.ThisWeek => (Started ?? Ended) is { } at && at >= now - ThisWeek,
            _ => true,
        };

    /// <summary>Lo que abarca el chip "Esta semana".</summary>
    public static readonly TimeSpan ThisWeek = TimeSpan.FromDays(7);

    public bool IsKill => Kind == RunSegmentKind.Kill;

    public bool IsWipe => Kind == RunSegmentKind.Wipe;

    public bool IsTrash => Kind == RunSegmentKind.Trash;

    /// <summary>Mi DPS en la fila del historial ("391k"), o una raya si no estuve en la pelea.</summary>
    public string LocalDpsText => LocalDps is { } dps ? ActorRowViewModel.Compact(dps) : "—";

    /// <summary>"4:12" o "1:02:05": el largo sin el cero de adelante. Sin segundos (filas armadas a mano), Duration.</summary>
    public string DurationText => Seconds is { } s ? ShortLength(TimeSpan.FromSeconds(Math.Round(s))) : Duration;

    private static string ShortLength(TimeSpan length) =>
        length.TotalHours >= 1
            ? $"{(int)length.TotalHours}:{length.Minutes:00}:{length.Seconds:00}"
            : $"{(int)length.TotalMinutes}:{length.Seconds:00}";

    /// <summary>"intento 3": el conteo de siempre contra ese objetivo, solo en las peleas de boss con nombre.</summary>
    public string AttemptLabel => Attempt > 0 && !IsTrash && Target != "Encuentro" ? $"intento {Attempt}" : string.Empty;

    /// <summary>El percentil sin la "P", para la columna con barra.</summary>
    public string PercentileNumber => Percentile is { } p ? p.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Largo de la barra del percentil, de 0 a 1.</summary>
    public double PercentileRatio => Math.Clamp((Percentile ?? 0) / 100.0, 0, 1);

    /// <summary>"✝ 2": muertes de la party; nada si no murió nadie.</summary>
    public string DeathsText => Deaths is > 0 and var d ? $"✝ {d}" : string.Empty;

    /// <summary>Lo que la fila no muestra: fecha y hora, daño total, comparación con el récord, personaje y nota completa.</summary>
    public string RowTip => string.Join(
        "\n",
        new[]
        {
            $"{When} · daño total {Total}{(Deaths is > 0 and var d ? $" · {d} {(d == 1 ? "muerte" : "muertes")}" : string.Empty)}",
            string.Join(" ", new[] { IsRecord ? "★ récord personal" : string.Empty, AttemptLabel, VsBest, FastestTag, CharacterTag }.Where(t => t.Length > 0)).TrimStart('·', ' '),
            HasNote ? $"Nota: {Note}" : string.Empty,
        }.Where(line => line.Length > 0));

    /// <summary>
    /// "+8%" en cada kill: mi DPS contra el intento anterior (kill o wipe) contra el mismo objetivo y con el mismo
    /// personaje. La lista llega de la más nueva a la más vieja.
    /// </summary>
    public static IReadOnlyList<HistoryItem> WithDeltas(IReadOnlyList<HistoryItem> newestFirst)
    {
        var result = newestFirst.ToArray();
        var previous = new Dictionary<(string Target, string? Character), double>();
        for (var i = result.Length - 1; i >= 0; i--)
        {
            var item = result[i];
            if (item.LocalDps is not { } dps || dps <= 0 || item.Target == "Encuentro")
            {
                continue;
            }

            var key = (item.Target, item.Character);
            if (item.IsKill && previous.TryGetValue(key, out var before))
            {
                var change = (dps / before) - 1;
                result[i] = item with { Delta = change.ToString("+0%;−0%", CultureInfo.CurrentCulture), DeltaPositive = change >= 0 };
            }

            previous[key] = dps;
        }

        return result;
    }

    public static HistoryItem From(EncounterSummary summary)
    {
        var start = DateTimeOffset.Parse(summary.StartedAt, CultureInfo.InvariantCulture).ToLocalTime();
        var end = DateTimeOffset.Parse(summary.EndedAt, CultureInfo.InvariantCulture).ToLocalTime();

        // TargetIsBoss no se guarda (sale de data/npcs.json en vivo): acá queda la regla de duración y daño al objetivo.
        var bossAttempt = BossRules.IsBossAttempt(
            summary.TargetKilled, summary.TargetName is not null, targetIsBoss: false, end - start,
            summary.MainTargetSeconds is { } main ? TimeSpan.FromSeconds(main) : null, summary.ActorsDamage, summary.MainTargetDamage);
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
            Seconds: (end - start).TotalSeconds,
            Started: start,
            Kind: BossRules.Classify(bossAttempt, summary.TargetKilled),
            Deaths: summary.Deaths);
    }
}
