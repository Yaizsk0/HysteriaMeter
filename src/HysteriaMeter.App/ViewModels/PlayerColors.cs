using System.Windows.Media;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Un color por jugador para distinguir las filas de un vistazo. Sale del nombre, así cada jugador
/// conserva su color entre peleas. El acento del tema (Gold de Theme.xaml o el del tema elegido) queda reservado
/// para el jugador local. Con "Colores por clase", el color sale de la clase (como en Details!), también para el local.
/// </summary>
public static class PlayerColors
{
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x4F, 0x8F, 0xE0), // azul
        Color.FromRgb(0xC0, 0x5C, 0xD6), // magenta
        Color.FromRgb(0x3F, 0xB8, 0x9F), // turquesa
        Color.FromRgb(0x8A, 0x6C, 0xE8), // violeta
        Color.FromRgb(0xE0, 0x6A, 0x5A), // coral
        Color.FromRgb(0x6C, 0xBF, 0x4F), // verde
        Color.FromRgb(0xE0, 0x8F, 0x3A), // naranja
        Color.FromRgb(0x5A, 0xB4, 0xD8), // celeste
    ];

    /// <summary>Gold de Themes/Theme.xaml (tema por defecto): el color del local hasta que App aplica el tema.</summary>
    public static Color DefaultAccent { get; } = Color.FromRgb(0xE8, 0xB0, 0x4A);

    // Alfa de las barras: ~33 %, como las filas del mock del rediseño.
    private const byte BarAlpha = 0x55;

    private static readonly Brush[] Accents = Palette.Select(c => Freeze(new SolidColorBrush(c))).ToArray();
    private static readonly Brush[] Bars = Palette.Select(c => Freeze(new SolidColorBrush(Color.FromArgb(BarAlpha, c.R, c.G, c.B)))).ToArray();

    public static Brush Local { get; private set; } = Freeze(new SolidColorBrush(DefaultAccent));

    public static Brush LocalBar { get; private set; } = Freeze(new SolidColorBrush(Color.FromArgb(BarAlpha, DefaultAccent.R, DefaultAccent.G, DefaultAccent.B)));

    /// <summary>
    /// El jugador local toma el acento del tema (App lo llama cada vez que aplica uno): así la barra, el rombo y la línea
    /// de su fila combinan con NameLocal y RowBorderLocal, que también siguen al acento. Las filas lo toman en su próxima
    /// actualización.
    /// </summary>
    public static void UseAccent(Color accent)
    {
        Local = Freeze(new SolidColorBrush(accent));
        LocalBar = Freeze(new SolidColorBrush(Color.FromArgb(BarAlpha, accent.R, accent.G, accent.B)));
    }

    public static Brush For(string name) => Accents[IndexOf(name)];

    public static Brush BarFor(string name) => Bars[IndexOf(name)];

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Color, (Brush Accent, Brush Bar)> ByColor = new();

    public static Brush ForClass(Color color) => OfColor(color).Accent;

    public static Brush BarForClass(Color color) => OfColor(color).Bar;

    private static (Brush Accent, Brush Bar) OfColor(Color color) => ByColor.GetOrAdd(color, c =>
        (Freeze(new SolidColorBrush(c)), Freeze(new SolidColorBrush(Color.FromArgb(BarAlpha, c.R, c.G, c.B)))));

    /// <summary>FNV-1a: estable entre ejecuciones (string.GetHashCode cambia en cada proceso).</summary>
    internal static int IndexOf(string name)
    {
        var hash = 2166136261u;
        foreach (var c in name)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (int)(hash % (uint)Palette.Length);
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
