using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Modo dungeon: el resumen de una run (de la entrada a la zona a la última pelea), con cada boss, sus intentos
/// y cuántas veces murió cada uno. Referencia visual: mock "Modo dungeon: resumen de la run".
/// </summary>
public sealed class DungeonSummaryViewModel
{
    /// <summary>Ancho en píxeles de la línea de tiempo.</summary>
    public const double TimelineWidth = 880;

    /// <summary>Ancho en píxeles de la barra más larga en cada boss.</summary>
    public const double BarWidth = 240;

    private readonly Action<Guid>? _openFight;
    private readonly bool _anonymize;
    private readonly string _exportFolder;

    /// <param name="summary">La run.</param>
    /// <param name="openFight">Abre una pelea guardada (clic en una celda de la matriz); sin él, las celdas no se abren.</param>
    /// <param name="anonymize">"Ocultar los nombres de los demás": el texto para Discord y el CSV van con "Yo" y "Jugador N".</param>
    /// <param name="exportFolder">Dónde guardar el CSV; por defecto, Exportes.</param>
    public DungeonSummaryViewModel(DungeonSummary summary, Action<Guid>? openFight = null, bool anonymize = false, string? exportFolder = null)
    {
        _openFight = openFight;
        _anonymize = anonymize;
        _exportFolder = exportFolder ?? MeterViewModel.ExportFolder;

        var culture = CultureInfo.CurrentCulture;
        Summary = summary;
        Title = $"RUN · ENTRADA {summary.Entered.ToLocalTime().ToString("dd/MM HH:mm", culture)}";
        Entered = summary.Entered.ToLocalTime().ToString("HH:mm:ss", culture);
        Left = summary.Left.ToLocalTime().ToString("HH:mm:ss", culture);
        Duration = Length(summary.Duration);
        Bosses = summary.BossesKilled.ToString(culture);
        Wipes = summary.Wipes.ToString(culture);
        Deaths = summary.Deaths.ToString(culture);

        var total = Math.Max(summary.Duration.TotalSeconds, 1);
        var killOf = summary.Bosses.Where(b => b.Killed).DistinctBy(b => b.DecidingId).ToDictionary(b => b.DecidingId);
        Timeline = [.. summary.Timeline.Select(s =>
        {
            var suffix = SegmentSuffix(s, killOf);
            return new SegmentItem(
                Math.Max(s.StartSecond / total * TimelineWidth, 0),
                Math.Max(s.Seconds / total * TimelineWidth, 3),
                s.Kind switch { RunSegmentKind.Kill => KillBrush, RunSegmentKind.Wipe => WipeBrush, _ => TrashBrush },
                suffix.Length > 0 ? $"{s.Label} {suffix}" : s.Label,
                $"{s.Label} · {Length(TimeSpan.FromSeconds(s.Seconds))} · {SegmentKind(s)}", s.Kind)
            {
                // "2400123" en vez de "NPC 2400123": en un tramo angosto, lo que distingue dos plantillas es el final.
                Name = FightTarget.Compact(s.Label),
                KeepEnd = FightTarget.IsNpcLabel(s.Label),
                Suffix = suffix,
            };
        })];

        Cards = [.. summary.Bosses.Select(b =>
        {
            var top = b.Players.Select(p => p.Dps).DefaultIfEmpty().Max();
            return new BossCard(
                b.Name,
                b.Killed ? $"KILL {Length(b.Duration)}" : "SIN KILL",
                b.Killed ? KillBrush : WipeBrush,
                $"Intentos {b.Attempts} · DPS party {ActorRowViewModel.Compact(b.PartyDps)} · Muertes {b.Deaths}",
                [.. b.Players.Select(p => new BossPlayerRow(
                    p.Name,
                    p.Dps > 0 ? ActorRowViewModel.Compact(p.Dps) : "—",
                    Skulls(p.Deaths),
                    top > 0 ? p.Dps / top * BarWidth : 0,
                    ClassBrush(p.ClassId, 0x55),
                    p.IsLocalPlayer))])
            {
                Killed = b.Killed, Duration = Length(b.Duration), Attempts = b.Attempts,
                PartyDps = ActorRowViewModel.Compact(b.PartyDps), Deaths = b.Deaths, Note = NoteOf(b),
            };
        })];

        Columns = [.. summary.Bosses.Select(b => b.Name.ToUpper(culture))];
        Rows = [.. summary.Players.Select(p =>
        {
            var cells = summary.Bosses.Select(b => b.Players.FirstOrDefault(l => l.Name == p.Name) is { } line
                ? Cell(b, (line.Dps > 0 ? ActorRowViewModel.Compact(line.Dps) : "—") + (line.Deaths > 0 ? $" ☠{line.Deaths}" : string.Empty), line.Deaths)
                : new PlayerBossCell(string.Empty, 0)).ToList();
            return new PlayerRow(p.Name, ClassBrush(p.ClassId, 0xFF), [.. cells.Select(c => c.Text)],
                p.Deaths > 0 ? $"☠ {p.Deaths}" : "—", ActorRowViewModel.Compact(p.Dps)) { StyledCells = cells };
        })];
    }

    /// <summary>Una celda de la matriz: abre el intento que cuenta de ese boss (el kill, o el último), el que da su DPS.</summary>
    private PlayerBossCell Cell(BossSummary boss, string text, int deaths)
    {
        var canOpen = _openFight is not null && boss.DecidingId != Guid.Empty;
        var attempt = boss.Killed ? "el kill" : "el último intento";
        return new PlayerBossCell(text, deaths)
        {
            FightId = boss.DecidingId,
            CanOpen = canOpen,
            Tip = canOpen ? $"{text} · clic: abre {attempt} contra {boss.Name}" : text,
        };
    }

    /// <summary>Abre en el overlay el intento de esa celda. Devuelve si había uno para abrir.</summary>
    public bool OpenFight(PlayerBossCell? cell)
    {
        if (cell is not { CanOpen: true } || _openFight is null)
        {
            return false;
        }

        _openFight(cell.FightId);
        return true;
    }

    /// <summary>
    /// Lo que va después del nombre en el segmento, como el mock ("Furia 3:58", "Furia ✕"): la duración de su tarjeta en un
    /// kill, ✕ en un wipe y nada en el trash.
    /// </summary>
    private static string SegmentSuffix(RunSegment segment, Dictionary<Guid, BossSummary> killOf) => segment.Kind switch
    {
        RunSegmentKind.Kill => Length(killOf.TryGetValue(segment.FightId, out var boss) ? boss.Duration : TimeSpan.FromSeconds(segment.Seconds)),
        RunSegmentKind.Wipe => "✕",
        _ => string.Empty,
    };

    private static string SegmentKind(RunSegment segment) => segment.Kind switch
    {
        RunSegmentKind.Kill => "kill",
        RunSegmentKind.Wipe => "wipe",
        _ when segment.Label == FightTarget.Unknown => "sin nombre ni plantilla: no se sabe si fue trash o un wipe",
        _ => "trash",
    };

    /// <summary>
    /// La nota al pie de cada boss, solo con lo que se midió: cada wipe con el tiempo al que cayó y la fase más baja de
    /// vida que se vio (75, 50 o 25 %); sin wipes, si el kill fue limpio (nadie murió) y quién hizo más DPS. "Kill al
    /// primer intento" solo con nombre o plantilla: un "Boss N" sin identidad pudo tener wipes que no se reconocen.
    /// </summary>
    internal static string NoteOf(BossSummary boss)
    {
        const int Listed = 3;
        if (boss.WipeList.Count > 0)
        {
            var wipes = boss.WipeList.Take(Listed).Select((w, i) =>
                $"Wipe {(i + 1).ToString(CultureInfo.InvariantCulture)} a los {Length(w.Duration)}"
                + (w.BelowPercent is { } percent ? $" (vida bajo el {percent.ToString(CultureInfo.InvariantCulture)}%)." : "."));
            var rest = boss.WipeList.Count - Listed;
            return string.Join(' ', rest > 0 ? wipes.Append($"Y {rest.ToString(CultureInfo.InvariantCulture)} wipes más.") : wipes);
        }

        if (!boss.Killed)
        {
            return string.Empty;
        }

        var kill = boss.Deaths == 0 ? "Kill limpio." : boss.Identified ? "Kill al primer intento." : string.Empty;
        var best = boss.Players.Where(p => p.Dps > 0).MaxBy(p => p.Dps) is { } top ? $"Mejor DPS: {top.Name}." : string.Empty;
        return string.Join(' ', new[] { kill, best }.Where(part => part.Length > 0));
    }

    /// <summary>La run como se comparte: con nombres anónimos si así se eligió.</summary>
    private DungeonSummary Shared => _anonymize ? EncounterExport.Anonymize(Summary) : Summary;

    /// <summary>
    /// "Exportar resumen": guarda la run en Exportes como CSV (una fila por jugador y boss, y la run completa). Devuelve
    /// la ruta del archivo, o el motivo si no se pudo guardar.
    /// </summary>
    public (string? Path, string? Error) Export()
    {
        try
        {
            Directory.CreateDirectory(_exportFolder);
            var path = Path.Combine(_exportFolder, $"{EncounterExport.RunFileName(Summary.Entered.ToLocalTime())}.csv");
            File.WriteAllText(path, EncounterExport.RunToCsv(Shared), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return (path, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"No se pudo guardar: {ex.Message}");
        }
    }

    public DungeonSummary Summary { get; }

    public string Title { get; }

    public string Entered { get; }

    public string Left { get; }

    public string Duration { get; }

    public string TimelineQuarter => Length(TimeSpan.FromTicks(Summary.Duration.Ticks / 4));

    public string TimelineHalf => Length(TimeSpan.FromTicks(Summary.Duration.Ticks / 2));

    public string TimelineThreeQuarters => Length(TimeSpan.FromTicks(Summary.Duration.Ticks / 4 * 3));

    public string Bosses { get; }

    public string Wipes { get; }

    public string Deaths { get; }

    public bool NoBosses => Cards.Count == 0;

    public IReadOnlyList<SegmentItem> Timeline { get; }

    public IReadOnlyList<BossCard> Cards { get; }

    /// <summary>Nombres de los bosses, para la cabecera de la tabla de jugadores.</summary>
    public IReadOnlyList<string> Columns { get; }

    public IReadOnlyList<PlayerRow> Rows { get; }

    /// <summary>El resumen en texto, para pegar en Discord (con nombres anónimos si así se eligió).</summary>
    public string ShareText
    {
        get
        {
            var text = new StringBuilder();
            text.AppendLine(CultureInfo.InvariantCulture, $"**{Title}** · {Duration} · {Bosses} bosses · {Wipes} wipes · {Deaths} muertes");
            foreach (var boss in Shared.Bosses)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"> **{boss.Name}** {(boss.Killed ? "kill " + Length(boss.Duration) : "sin kill")} · {boss.Attempts} intentos");
                foreach (var player in boss.Players)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"> {player.Name} {ActorRowViewModel.Compact(player.Dps)}{(player.Deaths > 0 ? " ☠" + player.Deaths : string.Empty)}");
                }
            }

            return text.ToString();
        }
    }

    // Mismos valores que Kill, Wipe, Neutral y ClassUnknown de Themes/Theme.xaml (no cambian con el tema).
    private static readonly Brush KillBrush = Frozen(Color.FromRgb(0xE8, 0xB0, 0x4A));
    private static readonly Brush WipeBrush = Frozen(Color.FromRgb(0xC0, 0x39, 0x2B));
    private static readonly Brush TrashBrush = Frozen(Color.FromRgb(0x3A, 0x3F, 0x4A));

    private static string Skulls(int deaths) => deaths switch { 0 => string.Empty, <= 4 => new string('☠', deaths), _ => $"☠×{deaths}" };

    private static SolidColorBrush ClassBrush(int? classId, byte alpha)
    {
        var color = PlayerClasses.Of(classId)?.Color ?? Color.FromRgb(0x9A, 0x96, 0x8C);
        return Frozen(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    internal static string Length(TimeSpan span) =>
        span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}

/// <summary>
/// Un tramo del timeline. <paramref name="Label"/> es el texto entero ("NPC 2400123 1:35"); la ventana dibuja
/// <see cref="Name"/>, que se recorta si no entra, y <see cref="Suffix"/> (duración o ✕), que siempre se ve.
/// </summary>
public sealed record SegmentItem(double Left, double Width, Brush Fill, string Label, string Tip, RunSegmentKind Kind = RunSegmentKind.Trash)
{
    public string Name { get; init; } = Label;

    /// <summary>
    /// El nombre es el número de una plantilla: si no entra, se corta el principio y no el final (los bosses de una
    /// dungeon suelen tener plantillas seguidas, 2400121 y 2400123). El rótulo completo está en <see cref="Tip"/>.
    /// </summary>
    public bool KeepEnd { get; init; }

    public string Suffix { get; init; } = string.Empty;
}

public sealed record BossCard(string Name, string Result, Brush ResultBrush, string Stats, IReadOnlyList<BossPlayerRow> Players)
{
    public bool Killed { get; init; }
    public string Duration { get; init; } = string.Empty;
    public int Attempts { get; init; }
    public string PartyDps { get; init; } = string.Empty;
    public int Deaths { get; init; }

    /// <summary>Nota al pie: los wipes con su tiempo y fase, o si el kill fue limpio.</summary>
    public string Note { get; init; } = string.Empty;
}

public sealed record BossPlayerRow(string Name, string Dps, string Skulls, double BarWidth, Brush Bar, bool IsLocalPlayer);

public sealed record PlayerRow(string Name, Brush Color, IReadOnlyList<string> Cells, string Deaths, string Dps)
{
    public IReadOnlyList<PlayerBossCell> StyledCells { get; init; } = [];
}

/// <summary>Una celda de la matriz: DPS y muertes de un jugador en un boss, y el intento que se abre con un clic.</summary>
public sealed record PlayerBossCell(string Text, int Deaths)
{
    /// <summary>El intento que cuenta de ese boss (el kill, o el último).</summary>
    public Guid FightId { get; init; }

    public bool CanOpen { get; init; }

    public string? Tip { get; init; }
}
