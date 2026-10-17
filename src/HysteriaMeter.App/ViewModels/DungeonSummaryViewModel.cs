using System.Globalization;
using System.Text;
using System.Windows.Media;
using HysteriaMeter.Combat;

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

    public DungeonSummaryViewModel(DungeonSummary summary)
    {
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
        Timeline = [.. summary.Timeline.Select(s => new SegmentItem(
            Math.Max(s.StartSecond / total * TimelineWidth, 0),
            Math.Max(s.Seconds / total * TimelineWidth, 3),
            s.Kind switch { RunSegmentKind.Kill => KillBrush, RunSegmentKind.Wipe => WipeBrush, _ => TrashBrush },
            s.Label,
            $"{s.Label} · {Length(TimeSpan.FromSeconds(s.Seconds))} · {(s.Kind switch { RunSegmentKind.Kill => "kill", RunSegmentKind.Wipe => "wipe", _ => "trash" })}", s.Kind))];

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
                PartyDps = ActorRowViewModel.Compact(b.PartyDps), Deaths = b.Deaths,
            };
        })];

        Columns = [.. summary.Bosses.Select(b => b.Name.ToUpper(culture))];
        Rows = [.. summary.Players.Select(p =>
        {
            var cells = summary.Bosses.Select(b => b.Players.FirstOrDefault(l => l.Name == p.Name) is { } line
                ? new PlayerBossCell((line.Dps > 0 ? ActorRowViewModel.Compact(line.Dps) : "—") + (line.Deaths > 0 ? $" ☠{line.Deaths}" : string.Empty), line.Deaths)
                : new PlayerBossCell(string.Empty, 0)).ToList();
            return new PlayerRow(p.Name, ClassBrush(p.ClassId, 0xFF), [.. cells.Select(c => c.Text)],
                p.Deaths > 0 ? $"☠ {p.Deaths}" : "—", ActorRowViewModel.Compact(p.Dps)) { StyledCells = cells };
        })];
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

    /// <summary>El resumen en texto, para pegar en Discord.</summary>
    public string ShareText
    {
        get
        {
            var text = new StringBuilder();
            text.AppendLine(CultureInfo.InvariantCulture, $"**{Title}** · {Duration} · {Bosses} bosses · {Wipes} wipes · {Deaths} muertes");
            foreach (var boss in Summary.Bosses)
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

public sealed record SegmentItem(double Left, double Width, Brush Fill, string Label, string Tip, RunSegmentKind Kind = RunSegmentKind.Trash);

public sealed record BossCard(string Name, string Result, Brush ResultBrush, string Stats, IReadOnlyList<BossPlayerRow> Players)
{
    public bool Killed { get; init; }
    public string Duration { get; init; } = string.Empty;
    public int Attempts { get; init; }
    public string PartyDps { get; init; } = string.Empty;
    public int Deaths { get; init; }
}

public sealed record BossPlayerRow(string Name, string Dps, string Skulls, double BarWidth, Brush Bar, bool IsLocalPlayer);

public sealed record PlayerRow(string Name, Brush Color, IReadOnlyList<string> Cells, string Deaths, string Dps)
{
    public IReadOnlyList<PlayerBossCell> StyledCells { get; init; } = [];
}

public sealed record PlayerBossCell(string Text, int Deaths);
