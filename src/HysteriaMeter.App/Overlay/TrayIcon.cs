using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using HysteriaMeter.App.ViewModels;
using DPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;

namespace HysteriaMeter.App.Overlay;

/// <summary>
/// Icono en la bandeja del sistema con un menú rápido: mostrar u ocultar el overlay (aunque esté
/// en modo atravesable), reiniciar la pelea, abrir la carpeta de datos y salir.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _image;

    public TrayIcon(OverlayWindows windows, MeterSession session)
    {
        _image = DrawIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Mostrar u ocultar el overlay (Ctrl+Shift+H)", null, (_, _) => ToggleVisible(windows));
        menu.Items.Add("Reiniciar la pelea (Ctrl+Shift+R)", null, (_, _) => session.Reset());
        menu.Items.Add("Abrir la carpeta de datos", null, (_, _) => OpenDataFolder());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Application.Current?.MainWindow?.Close());

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = $"Hysteria Meter {AppVersion.Text}",
            ContextMenuStrip = menu,
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

    /// <summary>Las ventanas se ocultan o muestran todas juntas, como con el atajo.</summary>
    private static void ToggleVisible(OverlayWindows windows)
    {
        var show = !windows.All.Any(w => w.IsVisible);
        windows.ForEach(w => w.Visibility = show ? Visibility.Visible : Visibility.Hidden);
        if (show)
        {
            windows.ForEach(w => w.Activate());
        }
    }

    private static void OpenDataFolder()
    {
        System.IO.Directory.CreateDirectory(AppPaths.DataFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataFolder}\"") { UseShellExecute = true });
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
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr handle);
    }
}
