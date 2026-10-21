using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using HysteriaMeter.App.Themes;
using HysteriaMeter.App.ViewModels;
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
        _image = LoadIcon();
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
    /// los atajos se actualizan cada vez que se abre: el atajo configurado va al lado de "Ocultar overlay". También la
    /// fuente y la escala de la interfaz (Configuración → Apariencia), que WinForms no toma del LayoutTransform de WPF.
    /// </summary>
    public static Forms.ContextMenuStrip CreateMenu(OverlayWindows windows, MeterSession session)
    {
        var colors = new TrayMenuColors();
        var renderer = new TrayMenuRenderer(colors);
        var menu = new Forms.ContextMenuStrip
        {
            Renderer = renderer,
            ShowImageMargin = false,
            ShowCheckMargin = false,
        };
        var menuFont = new MenuFont(session.Preferences.TextFont, UiScale.Normalize(session.Preferences.UiScale));
        menu.Font = menuFont.Value;
        menu.Disposed += (_, _) => menuFont.Dispose();

        var visibility = Item(menu, "Ocultar overlay", () => ToggleVisible(windows));
        var reset = Item(menu, "Reiniciar la pelea", session.Reset);
        Item(menu, "Historial", () => OpenPanel(windows, history: true));
        Item(menu, "Configuración", () => OpenPanel(windows, history: false));
        Item(menu, "Abrir la carpeta de datos", OpenDataFolder);
        menu.Items.Add(new Forms.ToolStripSeparator { AutoSize = false });
        var exit = Item(menu, "Salir", () => Application.Current?.MainWindow?.Close());
        exit.Tag = TrayMenuRenderer.DangerTag;
        Measure(menu, renderer, menuFont.Scale);

        menu.Opening += (_, _) =>
        {
            var scale = UiScale.Normalize(session.Preferences.UiScale);
            if (menuFont.Name != session.Preferences.TextFont || menuFont.Scale != scale)
            {
                var previous = menuFont;
                menuFont = new MenuFont(session.Preferences.TextFont, scale);
                menu.Font = menuFont.Value;
                previous.Dispose();
                Measure(menu, renderer, scale);
            }

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

    private static Forms.ToolStripMenuItem Item(Forms.ContextMenuStrip menu, string text, Action onClick)
    {
        var item = new Forms.ToolStripMenuItem(text, null, (_, _) => onClick());
        menu.Items.Add(item);
        return item;
    }

    /// <summary>
    /// Las medidas del mock (px a 96 ppp) por el DPI del monitor y la escala de la interfaz: ancho mínimo, relleno de
    /// los ítems, alto del separador y márgenes de los extremos. La fuente, en puntos, ya escala con el DPI.
    /// </summary>
    private static void Measure(Forms.ContextMenuStrip menu, TrayMenuRenderer renderer, double uiScale)
    {
        renderer.UiScale = uiScale;
        var scale = menu.DeviceDpi / 96.0 * uiScale;
        int Px(double value) => (int)Math.Round(value * scale);

        menu.MinimumSize = new System.Drawing.Size(Px(220), 0);
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            if (item is Forms.ToolStripSeparator separator)
            {
                separator.Height = Px(9);
            }
            else
            {
                // Alto de 34 px como el mock: el texto mide unos 20, el resto es relleno. La posición del texto a los
                // costados la decide TrayMenuRenderer (WinForms ignora el relleno izquierdo para ubicarlo).
                item.Padding = new Forms.Padding(Px(4), Px(7), 0, Px(7));
            }
        }

        // El menú de WinForms recalcula su Padding (deja 2 px arriba y abajo): los 6 px del mock van como margen
        // del primer y el último ítem. A los costados, el renderer deja el mismo margen al resaltar.
        var edge = (int)Math.Round(((TrayMenuRenderer.Inset * uiScale) - 2) * menu.DeviceDpi / 96.0);
        menu.Items[0].Margin = new Forms.Padding(0, edge, 0, 0);
        menu.Items[^1].Margin = new Forms.Padding(0, 0, 0, edge);
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
    /// El ícono de la app (Assets/HysteriaMeter.ico, el mismo del .exe) en el tamaño de la bandeja para el DPI de
    /// Windows: el .ico trae 16, 20, 24, 32 y 40 px, así no se agranda uno chico. Fijo: no sigue al tema.
    /// </summary>
    private static Icon LoadIcon()
    {
        try
        {
            using var stream = Application.GetResourceStream(new Uri("/HysteriaMeter;component/Assets/HysteriaMeter.ico", UriKind.Relative))?.Stream;
            if (stream is not null)
            {
                return new Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // Sin el recurso queda el ícono genérico de Windows: la bandeja sigue funcionando.
        }

        return (Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }

    /// <summary>
    /// La fuente elegida, también en GDI: ToolStrip mide con GDI y PrivateFontCollection solo la expone a GDI+.
    /// El registro en memoria es privado al proceso; se libera con el menú o al cambiar de fuente.
    /// </summary>
    private sealed class MenuFont : IDisposable
    {
        private PrivateFontCollection? _collection;
        private FontFamily? _family;
        private IntPtr _memory;
        private IntPtr _registration;

        public MenuFont(string name, double scale)
        {
            Name = name;
            Scale = scale;
            // 13 px lógicos a 96 ppp, por la escala de la interfaz; puntos para que WinForms escale con el DPI del monitor.
            var size = (float)(9.75 * scale);
            Value = new Font(name, size, System.Drawing.FontStyle.Regular, GraphicsUnit.Point);
            var file = name switch
            {
                "IBM Plex Sans" => "IBMPlexSans/IBMPlexSans-Regular.ttf",
                "Barlow Condensed" => "BarlowCondensed/BarlowCondensed-Regular.ttf",
                "Inter" => "Inter-Regular.ttf",
                "Barlow Semi Condensed" => "BarlowSemiCondensed-Regular.ttf",
                _ => null,
            };
            if (file is null)
            {
                return;
            }

            try
            {
                using var stream = Application.GetResourceStream(new Uri($"/HysteriaMeter;component/Assets/Fonts/{file}", UriKind.Relative))?.Stream;
                if (stream is null)
                {
                    return;
                }

                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                var data = bytes.ToArray();
                _memory = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, _memory, data.Length);
                uint count = 0;
                _registration = NativeMethods.AddFontMemResourceEx(_memory, (uint)data.Length, IntPtr.Zero, ref count);
                if (_registration == IntPtr.Zero)
                {
                    ReleaseResources();
                    return;
                }

                _collection = new PrivateFontCollection();
                _collection.AddMemoryFont(_memory, data.Length);
                _family = _collection.Families[0];
                var embedded = new Font(_family, size, System.Drawing.FontStyle.Regular, GraphicsUnit.Point);
                Value.Dispose();
                Value = embedded;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException)
            {
                // Si falta o no se puede cargar el recurso, queda la familia elegida resuelta por Windows.
                ReleaseResources();
            }
        }

        public string Name { get; }

        public double Scale { get; }

        public Font Value { get; }

        public void Dispose()
        {
            Value.Dispose();
            ReleaseResources();
        }

        private void ReleaseResources()
        {
            _family?.Dispose();
            _family = null;
            _collection?.Dispose();
            _collection = null;
            if (_registration != IntPtr.Zero)
            {
                NativeMethods.RemoveFontMemResourceEx(_registration);
                _registration = IntPtr.Zero;
            }

            if (_memory != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_memory);
                _memory = IntPtr.Zero;
            }
        }
    }

    private static class NativeMethods
    {
        [DllImport("gdi32.dll")]
        public static extern IntPtr AddFontMemResourceEx(IntPtr data, uint length, IntPtr reserved, ref uint count);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveFontMemResourceEx(IntPtr handle);

        public const int WindowCornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
        public const int BorderColor = 34;            // DWMWA_BORDER_COLOR
        public const int CornerRound = 2;             // DWMWCP_ROUND

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

    /// <summary>Escala de la interfaz (1 = 100 %): los márgenes y el radio del resaltado crecen con el texto.</summary>
    public double UiScale { get; set; } = 1;

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

        var scale = Scale(e.ToolStrip);
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
            var scale = Scale(e.ToolStrip);
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
        var (left, right) = Inside(e.ToolStrip, e.Item, Scale(e.ToolStrip));
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, left, y, right, y);
    }

    /// <summary>Píxeles por px del mock: el DPI del monitor por la escala de la interfaz.</summary>
    private float Scale(Forms.ToolStrip? strip) => (float)((strip?.DeviceDpi ?? 96) / 96.0 * UiScale);

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
