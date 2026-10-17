using System.Windows;
using System.Windows.Media;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Un tema visual: reemplaza los colores de <c>Themes/Theme.xaml</c> por clave.</summary>
public sealed record MeterTheme(string Name, string Description, IReadOnlyDictionary<string, Color> Colors);

/// <summary>
/// Temas del overlay, como los de Ember (Minimal, Classic...). "Dorado" es el de los mocks del rediseño y el de siempre.
/// Un tema cambia los fondos, el acento y los textos. Los neutros (filas, filetes, bordes) son blanco translúcido y no
/// dependen del tema; los semánticos (curación, peligro, percentiles...) tampoco.
/// </summary>
public static class MeterThemes
{
    public const string Default = "Dorado";

    public static IReadOnlyList<MeterTheme> All { get; } =
    [
        new(Default, "Oro y negro, estilo AION", new Dictionary<string, Color>()),
        new("Mínimo", "Gris neutro, sin dorados: no distrae", Palette(
            background: "#101214", accent: "#D6DAE0", text: "#E2E4E8", bright: "#FFFFFF", muted: "#AEB3BA", dim: "#8E949C")),
        new("Abismo", "Violeta y azul, como el Abismo", Palette(
            background: "#0C0A18", accent: "#B9A2F0", text: "#E4E0F2", bright: "#F2EEFF", muted: "#B0A8C8", dim: "#948CB0")),
        new("Carmesí", "Rojo oscuro, para los de Asmodae", Palette(
            background: "#140A0C", accent: "#F0A08C", text: "#F0E2DE", bright: "#FFEEE8", muted: "#C8AAA2", dim: "#B08C84")),
    ];

    public static MeterTheme Of(string? name) => All.FirstOrDefault(t => t.Name == name) ?? All[0];

    /// <summary>
    /// Pone los colores del tema en los recursos de la aplicación. Las ventanas que se abren después lo toman entero;
    /// en las abiertas cambian los pinceles que se pueden modificar.
    /// Las claves derivadas del acento (<see cref="AccentDerived"/>) que el tema no define toman el color de su
    /// <c>Gold</c> con el alfa del valor por defecto: así todo el sistema sigue al acento sin repetirlo en cada tema.
    /// </summary>
    public static void Apply(ResourceDictionary resources, string? name, IReadOnlyDictionary<string, Color> defaults)
    {
        var theme = Of(name);
        Color? accent = theme.Colors.TryGetValue("Gold", out var a) ? a : defaults.TryGetValue("Gold", out var d) ? d : null;
        foreach (var (key, fallback) in defaults)
        {
            var color = theme.Colors.TryGetValue(key, out var c) ? c
                : accent is { } gold && AccentDerived.Contains(key) ? Color.FromArgb(fallback.A, gold.R, gold.G, gold.B)
                : fallback;
            if (resources[key] is SolidColorBrush { IsFrozen: false } brush)
            {
                brush.Color = color;
            }
            else
            {
                resources[key] = new SolidColorBrush(color);
            }
        }
    }

    /// <summary>Los colores originales de <c>Theme.xaml</c>, para volver al tema por defecto sin reiniciar.</summary>
    public static IReadOnlyDictionary<string, Color> Snapshot(ResourceDictionary resources) =>
        Keys.Where(k => resources[k] is SolidColorBrush).ToDictionary(k => k, k => ((SolidColorBrush)resources[k]).Color);

    /// <summary>Claves que siguen al acento del tema conservando el alfa de <c>Theme.xaml</c>.</summary>
    private static readonly string[] AccentDerived =
        ["GoldDim", "AccentFaint", "AccentSoft", "AccentTint", "AccentLine", "AccentStrong", "RowBorderLocal", "NameLocal"];

    /// <summary>Todas las claves que puede cambiar un tema: las que define cada paleta y las derivadas del acento.</summary>
    private static readonly string[] Keys =
    [
        "WindowBg", "Panel", "PanelRaised", "PanelSolid", "Surface", "CardBg", "InputBg", "MenuBg",
        "Gold", "GoldBright", "Text", "TextBright", "TextMuted", "TextDim",
        .. AccentDerived,
    ];

    /// <summary>
    /// Paleta de un tema a partir de su fondo base y su acento. Los alfas de los paneles y los escalones de las superficies
    /// copian los del tema por defecto (#0C0E13): panel 86 %, elevado 92 %, sólido 95 %; superficie, tarjeta, input y menú
    /// un poco más claros que el fondo. El acento brillante (hover, % de vida) es el acento un 25 % más cerca del blanco.
    /// </summary>
    private static Dictionary<string, Color> Palette(string background, string accent, string text, string bright, string muted, string dim)
    {
        var bg = Parse(background);
        var gold = Parse(accent);
        return new()
        {
            ["WindowBg"] = bg,
            ["Panel"] = WithAlpha(bg, 0xDB),
            ["PanelRaised"] = WithAlpha(bg, 0xEB),
            ["PanelSolid"] = WithAlpha(bg, 0xF2),
            ["Surface"] = Lift(bg, 3, 4, 5),
            ["CardBg"] = Lift(bg, 6, 7, 9),
            ["InputBg"] = Lift(bg, 10, 12, 15),
            ["MenuBg"] = Lift(bg, 14, 16, 20),
            ["Gold"] = gold,
            ["GoldBright"] = Color.FromRgb(Toward(gold.R, 255), Toward(gold.G, 255), Toward(gold.B, 255)),
            ["Text"] = Parse(text),
            ["TextBright"] = Parse(bright),
            ["TextMuted"] = Parse(muted),
            ["TextDim"] = Parse(dim),
        };
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Lift(Color color, int r, int g, int b) =>
        Color.FromRgb((byte)Math.Min(255, color.R + r), (byte)Math.Min(255, color.G + g), (byte)Math.Min(255, color.B + b));

    private static byte Toward(byte from, byte to) => (byte)Math.Round(from + (to - from) * 0.25);

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
