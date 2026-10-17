using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using HysteriaMeter.App.ViewModels;
using DPoint = System.Drawing.Point;
using DrawingSystemColors = System.Drawing.SystemColors;
using Forms = System.Windows.Forms;

namespace HysteriaMeter.App.Overlay;

/// <summary>
/// Icono en la bandeja del sistema con un menú rápido (mock Avisos): mostrar u ocultar el overlay (aunque esté en modo
/// atravesable), reiniciar la pelea, abrir el historial o la configuración, abrir la carpeta de datos y salir.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _image;

    public TrayIcon(OverlayWindows windows, MeterSession session)
    {
        _image = DrawIcon();
        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = $"Hysteria Meter {AppVersion.Text}",
            ContextMenuStrip = CreateMenu(windows, session),
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                ToggleVisible(windows);
            }
        };
    }

    /// <summary>
    /// El menú con los colores del tema (MenuBg, borde Stroke, resaltado AccentSoft, "Salir" en Danger). Los textos y
    /// los atajos se actualizan cada vez que se abre: el atajo configurado va al lado de "Ocultar overlay".
    /// </summary>
    public static Forms.ContextMenuStrip CreateMenu(OverlayWindows windows, MeterSession session)
    {
        var colors = new TrayMenuColors();
        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new TrayMenuRenderer(colors),
            ShowImageMargin = false,
            ShowCheckMargin = false,
            // Inter es un recurso de WPF y GDI no la ve: Segoe UI a 13 px (9,75 pt), el tamaño del mock.
            Font = new Font("Segoe UI", 9.75f),
        };
        var scale = menu.DeviceDpi / 96f;
        menu.MinimumSize = new System.Drawing.Size((int)Math.Round(220 * scale), 0);

        var visibility = Item(menu, scale, "Ocultar overlay", () => ToggleVisible(windows));
        var reset = Item(menu, scale, "Reiniciar la pelea", session.Reset);
        Item(menu, scale, "Historial", () => OpenPanel(windows, history: true));
        Item(menu, scale, "Configuración", () => OpenPanel(windows, history: false));
        Item(menu, scale, "Abrir la carpeta de datos", OpenDataFolder);
        menu.Items.Add(new Forms.ToolStripSeparator { AutoSize = false, Height = (int)Math.Round(9 * scale) });
        var exit = Item(menu, scale, "Salir", () => Application.Current?.MainWindow?.Close());
        exit.Tag = TrayMenuRenderer.DangerTag;

        // El menú de WinForms recalcula su Padding (deja 2 px arriba y abajo): los 6 px del mock van como margen
        // del primer y el último ítem. A los costados, el renderer deja el mismo margen al resaltar.
        var edge = (int)Math.Round((TrayMenuRenderer.Inset - 2) * scale);
        visibility.Margin = new Forms.Padding(0, edge, 0, 0);
        exit.Margin = new Forms.Padding(0, 0, 0, edge);

        menu.Opening += (_, _) =>
        {
            colors.Refresh();
            menu.BackColor = colors.Background;
            RoundCorners(menu, colors.Border);
            visibility.Text = VisibilityText(windows.All.Any(w => w.IsVisible));
            visibility.ShortcutKeyDisplayString = session.Preferences.HotkeyVisibility;
            reset.ShortcutKeyDisplayString = session.Preferences.HotkeyReset;
        };
        menu.HandleCreated += (_, _) => RoundCorners(menu, colors.Border);
        return menu;
    }

    /// <summary>
    /// <paramref name="over"/> (con su alfa) pintado encima de <paramref name="under"/> (opaco): WinForms no mezcla
    /// transparencias, así que los colores del tema con alfa se pasan a opacos sobre el fondo del menú.
    /// </summary>
    public static Color Blend(Color over, Color under)
    {
        var alpha = over.A / 255.0;
        int Mix(int top, int bottom) => (int)Math.Round((top * alpha) + (bottom * (1 - alpha)));
        return Color.FromArgb(Mix(over.R, under.R), Mix(over.G, under.G), Mix(over.B, under.B));
    }

    /// <summary>"Ocultar overlay" si se ve alguna ventana; si están todas ocultas, "Mostrar overlay".</summary>
    public static string VisibilityText(bool anyVisible) => anyVisible ? "Ocultar overlay" : "Mostrar overlay";

    private static Forms.ToolStripMenuItem Item(Forms.ContextMenuStrip menu, float scale, string text, Action onClick)
    {
        // Alto de 34 px como el mock: el texto mide unos 20, el resto es relleno. La posición del texto a los
        // costados la decide TrayMenuRenderer (WinForms ignora el relleno izquierdo para ubicarlo).
        var vertical = (int)Math.Round(7 * scale);
        var item = new Forms.ToolStripMenuItem(text, null, (_, _) => onClick())
        {
            Padding = new Forms.Padding((int)Math.Round(4 * scale), vertical, 0, vertical),
        };
        menu.Items.Add(item);
        return item;
    }

    /// <summary>Las ventanas se ocultan o muestran todas juntas, como con el atajo.</summary>
    private static void ToggleVisible(OverlayWindows windows)
    {
        var show = !windows.All.Any(w => w.IsVisible);
        ShowAll(windows, show);
    }

    private static void ShowAll(OverlayWindows windows, bool show)
    {
        windows.ForEach(w => w.Visibility = show ? Visibility.Visible : Visibility.Hidden);
        if (show)
        {
            windows.ForEach(w => w.Activate());
        }
    }

    /// <summary>
    /// Abre el historial o la configuración en la ventana principal, con el mismo comando que su botón del encabezado.
    /// Si ya estaba abierto no lo cierra; si el overlay estaba oculto o minimizado, lo muestra entero. Si dejaba pasar
    /// los clics al juego, se desbloquea (todas las ventanas, como el candado): si no, el panel no se podría usar.
    /// </summary>
    private static void OpenPanel(OverlayWindows windows, bool history)
    {
        if (windows.All.Count == 0 || windows.All[0].DataContext is not MeterViewModel main)
        {
            return;
        }

        ShowAll(windows, show: true);
        if (main.IsClickThrough)
        {
            windows.ForEach(w => w.SetClickThrough(false));
        }

        main.IsMinimized = false;
        if (history && !main.IsHistoryOpen)
        {
            main.ToggleHistoryCommand.Execute(null);
        }
        else if (!history && !main.IsSettingsOpen)
        {
            main.ToggleSettingsCommand.Execute(null);
        }
    }

    private static void OpenDataFolder()
    {
        System.IO.Directory.CreateDirectory(AppPaths.DataFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataFolder}\"") { UseShellExecute = true });
    }

    /// <summary>
    /// Esquinas redondeadas (radio de 8 px) y borde del color Stroke en Windows 11. En Windows 10 la llamada no hace
    /// nada y queda el borde recto que dibuja el renderer.
    /// </summary>
    private static void RoundCorners(Forms.ContextMenuStrip menu, Color border)
    {
        if (!menu.IsHandleCreated)
        {
            return;
        }

        var round = NativeMethods.CornerRound;
        _ = NativeMethods.DwmSetWindowAttribute(menu.Handle, NativeMethods.WindowCornerPreference, ref round, sizeof(int));
        var colorRef = border.R | (border.G << 8) | (border.B << 16);
        _ = NativeMethods.DwmSetWindowAttribute(menu.Handle, NativeMethods.BorderColor, ref colorRef, sizeof(int));
    }

    /// <summary>
    /// Un rombo como el del encabezado del overlay, dibujado en memoria (sin archivos .ico): acento #E8B04A (Gold de
    /// Theme.xaml) sobre el fondo #0C0E13 (WindowBg). Fijo: se dibuja una vez, antes de que haya tema.
    /// </summary>
    private static Icon DrawIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            DPoint[] diamond = [new(16, 2), new(30, 16), new(16, 30), new(2, 16)];
            using var fill = new SolidBrush(Color.FromArgb(0x0C, 0x0E, 0x13));
            using var pen = new Pen(Color.FromArgb(0xE8, 0xB0, 0x4A), 3);
            g.FillPolygon(fill, diamond);
            g.DrawPolygon(pen, diamond);
            using var inner = new SolidBrush(Color.FromArgb(0xE8, 0xB0, 0x4A));
            g.FillPolygon(inner, [new DPoint(16, 10), new DPoint(22, 16), new DPoint(16, 22), new DPoint(10, 16)]);
        }

        var handle = bitmap.GetHicon();
        using var temporary = Icon.FromHandle(handle);
        var icon = (Icon)temporary.Clone();
        NativeMethods.DestroyIcon(handle);
        return icon;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }

    private static class NativeMethods
    {
        public const int WindowCornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
        public const int BorderColor = 34;            // DWMWA_BORDER_COLOR
        public const int CornerRound = 2;             // DWMWCP_ROUND

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr handle);

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}

/// <summary>
/// Colores del menú de la bandeja, leídos del tema (Theme.xaml, con lo que cambie MeterThemes) al crearlo y cada vez
/// que se abre: MenuBg, Stroke, Track, AccentSoft, Text, TextDim y Danger. WinForms no mezcla transparencias sobre el
/// fondo: los colores con alfa se pasan a opacos sobre MenuBg.
/// </summary>
public sealed class TrayMenuColors : Forms.ProfessionalColorTable
{
    public TrayMenuColors()
    {
        UseSystemColors = false;

        // Sin App (una vista previa suelta) no hay tema: quedan los colores de menú de Windows, sin copiar la paleta.
        Background = DrawingSystemColors.Menu;
        Border = DrawingSystemColors.ControlDark;
        Separator = DrawingSystemColors.ControlLight;
        Hover = DrawingSystemColors.MenuHighlight;
        Text = DrawingSystemColors.MenuText;
        TextDim = DrawingSystemColors.GrayText;
        Danger = DrawingSystemColors.MenuText;
        Refresh();
    }

    public Color Background { get; private set; }

    public Color Border { get; private set; }

    public Color Separator { get; private set; }

    public Color Hover { get; private set; }

    public Color Text { get; private set; }

    public Color TextDim { get; private set; }

    public Color Danger { get; private set; }

    /// <summary>Vuelve a leer el tema de la App (si hay): así el menú sigue a Carmesí, Abismo o Mínimo.</summary>
    public void Refresh()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        Background = Opaque(Find(app, "MenuBg") ?? Background, DrawingSystemColors.Menu);
        Border = Find(app, "Stroke") is { } stroke ? TrayIcon.Blend(stroke, Background) : Border;
        Separator = Find(app, "Track") is { } track ? TrayIcon.Blend(track, Background) : Separator;
        Hover = Find(app, "AccentSoft") is { } soft ? TrayIcon.Blend(soft, Background) : Hover;
        Text = Find(app, "Text") is { } text ? TrayIcon.Blend(text, Background) : Text;
        TextDim = Find(app, "TextDim") is { } dim ? TrayIcon.Blend(dim, Background) : TextDim;
        Danger = Find(app, "Danger") is { } danger ? TrayIcon.Blend(danger, Background) : Danger;
    }

    private static Color Opaque(Color color, Color under) => color.A == 255 ? color : TrayIcon.Blend(color, under);

    private static Color? Find(Application app, string key) =>
        app.TryFindResource(key) is System.Windows.Media.SolidColorBrush { Color: var c } ? Color.FromArgb(c.A, c.R, c.G, c.B) : null;

    public override Color ToolStripDropDownBackground => Background;

    public override Color MenuBorder => Border;

    public override Color MenuItemBorder => Hover;

    public override Color MenuItemSelected => Hover;

    public override Color MenuItemSelectedGradientBegin => Hover;

    public override Color MenuItemSelectedGradientEnd => Hover;

    public override Color ImageMarginGradientBegin => Background;

    public override Color ImageMarginGradientMiddle => Background;

    public override Color ImageMarginGradientEnd => Background;

    public override Color SeparatorDark => Separator;

    public override Color SeparatorLight => Separator;
}

/// <summary>Dibuja el menú como el mock: fondo plano, resaltado redondeado, separador fino y "Salir" en rojo.</summary>
public sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    /// <summary>Marca de los ítems destructivos ("Salir").</summary>
    public const string DangerTag = "danger";

    /// <summary>Margen (en px a 96 ppp) entre el borde del menú y el resaltado o el separador, como el padding del mock.</summary>
    public const int Inset = 6;

    /// <summary>Relleno del texto dentro del resaltado (px a 96 ppp): el texto queda a 16 px del borde, como el mock.</summary>
    public const int TextPadding = 10;

    private readonly TrayMenuColors _colors;

    public TrayMenuRenderer(TrayMenuColors colors)
        : base(colors)
    {
        _colors = colors;
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_colors.Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(_colors.Border);
        var bounds = e.ToolStrip.ClientRectangle;
        e.Graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
    {
        // Sin margen de íconos: no se pinta la franja gris de WinForms.
    }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
        {
            return;
        }

        var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        var (left, right) = Inside(e.ToolStrip, e.Item, scale);
        using var path = Rounded(new Rectangle(left, 0, right - left, e.Item.Height - 1), (int)Math.Round(5 * scale));
        using var brush = new SolidBrush(_colors.Hover);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        var shortcut = e.Item is Forms.ToolStripMenuItem { ShortcutKeyDisplayString: { Length: > 0 } keys } && e.Text == keys;
        e.TextColor = shortcut ? _colors.TextDim : Equals(e.Item.Tag, DangerTag) ? _colors.Danger : _colors.Text;
        if (e.Item is Forms.ToolStripMenuItem)
        {
            // El texto a TextPadding del resaltado; el atajo, alineado a la derecha con el mismo margen.
            var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
            var (left, right) = Inside(e.ToolStrip, e.Item, scale);
            var padding = (int)Math.Round(TextPadding * scale);
            var text = e.TextRectangle;
            e.TextRectangle = shortcut
                ? new Rectangle(right - padding - text.Width, text.Y, text.Width, text.Height)
                : new Rectangle(left + padding, text.Y, text.Width, text.Height);
        }

        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_colors.Separator);
        var (left, right) = Inside(e.ToolStrip, e.Item, (e.ToolStrip?.DeviceDpi ?? 96) / 96f);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, left, y, right, y);
    }

    /// <summary>
    /// De dónde a dónde pintar dentro del ítem (en sus coordenadas): el ancho del menú menos <see cref="Inset"/> por lado.
    /// Los ítems de WinForms ocupan todo el ancho (hasta un poco más) e ignoran el Padding del menú.
    /// </summary>
    private static (int Left, int Right) Inside(Forms.ToolStrip? strip, Forms.ToolStripItem item, float scale)
    {
        var inset = (int)Math.Round(Inset * scale);
        var width = strip?.ClientSize.Width ?? item.Width;
        return (inset - item.Bounds.X, width - inset - 1 - item.Bounds.X);
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
