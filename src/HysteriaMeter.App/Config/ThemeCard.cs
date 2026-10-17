using System.Windows;
using System.Windows.Media;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Config;

/// <summary>
/// Tarjeta de un tema en Configuración → Apariencia. La vista previa usa la paleta real del tema
/// (<see cref="MeterThemes.Resolve"/>): el fondo de las ventanas de suelo y tres barras del acento.
/// </summary>
public sealed record ThemeCard(MeterTheme Theme, Brush Ground, Brush Bar)
{
    /// <summary>Alfa de las barras de la vista previa (el 60 % del mock).</summary>
    private const byte BarAlpha = 0x99;

    /// <summary>Una tarjeta por tema, en el orden de <see cref="MeterThemes.All"/>.</summary>
    public static IReadOnlyList<ThemeCard> All()
    {
        var defaults = Defaults();
        return [.. MeterThemes.All.Select(theme => From(theme, MeterThemes.Resolve(theme.Name, defaults)))];
    }

    public static ThemeCard From(MeterTheme theme, IReadOnlyDictionary<string, Color> palette)
    {
        var ground = palette.TryGetValue("WindowBg", out var bg) ? bg : Colors.Transparent;
        var bar = palette.TryGetValue("Gold", out var gold) ? Color.FromArgb(BarAlpha, gold.R, gold.G, gold.B) : Colors.Transparent;
        return new ThemeCard(theme, Frozen(ground), Frozen(bar));
    }

    /// <summary>
    /// Los colores de Theme.xaml: los que guardó App al arrancar. Antes de eso (en el arnés y en los tests) los
    /// recursos de la App todavía no tienen ningún tema aplicado y valen lo mismo.
    /// </summary>
    private static IReadOnlyDictionary<string, Color> Defaults() =>
        MeterThemes.Defaults.Count > 0 || Application.Current is not { } app ? MeterThemes.Defaults : MeterThemes.Snapshot(app.Resources);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
