using System.Windows;
using System.Windows.Media;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Un tema visual: reemplaza los colores de <c>Themes/Theme.xaml</c> por clave.</summary>
public sealed record MeterTheme(string Name, string Description, IReadOnlyDictionary<string, Color> Colors);

/// <summary>
/// Temas del overlay, como los de Ember (Minimal, Classic...). "Dorado" es el de los mocks aprobados y el de siempre.
/// </summary>
public static class MeterThemes
{
    public const string Default = "Dorado";

    public static IReadOnlyList<MeterTheme> All { get; } =
    [
        new(Default, "Oro y negro, estilo AION", new Dictionary<string, Color>()),
        new("Mínimo", "Gris neutro, sin dorados: no distrae", Palette(
            panel: "#E6101214", solid: "#F7101214", accent: "#D6DAE0", accentDim: "#4A5058", rule: "#2C3036", ruleDim: "#202328",
            text: "#E2E4E8", bright: "#FFFFFF", muted: "#AEB3BA", dim: "#8E949C", row: "#181B1F", rowBorder: "#262A30",
            rowLocal: "#7A828C", nameLocal: "#FFFFFF")),
        new("Abismo", "Violeta y azul, como el Abismo", Palette(
            panel: "#E60C0A18", solid: "#F70C0A18", accent: "#B9A2F0", accentDim: "#4C3F78", rule: "#2E2748", ruleDim: "#221D36",
            text: "#E4E0F2", bright: "#F2EEFF", muted: "#B0A8C8", dim: "#948CB0", row: "#151226", rowBorder: "#241F3C",
            rowLocal: "#6E5AB0", nameLocal: "#D7C8FF")),
        new("Carmesí", "Rojo oscuro, para los de Asmodae", Palette(
            panel: "#E6140A0C", solid: "#F7140A0C", accent: "#F0A08C", accentDim: "#783A30", rule: "#48261F", ruleDim: "#361C18",
            text: "#F0E2DE", bright: "#FFEEE8", muted: "#C8AAA2", dim: "#B08C84", row: "#22120F", rowBorder: "#3A201B",
            rowLocal: "#A85A48", nameLocal: "#FFC8B8")),
    ];

    public static MeterTheme Of(string? name) => All.FirstOrDefault(t => t.Name == name) ?? All[0];

    /// <summary>
    /// Pone los colores del tema en los recursos de la aplicación. Las ventanas que se abren después lo toman entero;
    /// en las abiertas cambian los pinceles que se pueden modificar.
    /// </summary>
    public static void Apply(ResourceDictionary resources, string? name, IReadOnlyDictionary<string, Color> defaults)
    {
        var theme = Of(name);
        foreach (var (key, fallback) in defaults)
        {
            var color = theme.Colors.TryGetValue(key, out var c) ? c : fallback;
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

    private static readonly string[] Keys =
        ["Panel", "PanelSolid", "Gold", "GoldDim", "Rule", "RuleDim", "Text", "TextBright", "TextMuted", "TextDim", "Row", "RowBorder", "RowBorderLocal", "NameLocal"];

    private static Dictionary<string, Color> Palette(
        string panel, string solid, string accent, string accentDim, string rule, string ruleDim, string text, string bright,
        string muted, string dim, string row, string rowBorder, string rowLocal, string nameLocal) => new()
        {
            ["Panel"] = Parse(panel),
            ["PanelSolid"] = Parse(solid),
            ["Gold"] = Parse(accent),
            ["GoldDim"] = Parse(accentDim),
            ["Rule"] = Parse(rule),
            ["RuleDim"] = Parse(ruleDim),
            ["Text"] = Parse(text),
            ["TextBright"] = Parse(bright),
            ["TextMuted"] = Parse(muted),
            ["TextDim"] = Parse(dim),
            ["Row"] = Parse(row),
            ["RowBorder"] = Parse(rowBorder),
            ["RowBorderLocal"] = Parse(rowLocal),
            ["NameLocal"] = Parse(nameLocal),
        };

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
