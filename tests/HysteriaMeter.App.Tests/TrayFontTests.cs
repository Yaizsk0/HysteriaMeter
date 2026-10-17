using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using Forms = System.Windows.Forms;

namespace HysteriaMeter.App.Tests;

/// <summary>El menú usa la fuente embebida también al medir con GDI y libera la que ya no necesita.</summary>
public sealed class TrayFontTests
{
    private static readonly string[] FontChanges = ["Barlow Condensed", "Segoe UI", "IBM Plex Sans"];

    [Theory]
    [InlineData("IBM Plex Sans")]
    [InlineData("Barlow Condensed")]
    public void Bundled_menu_font_resolves_in_the_GDI_text_renderer(string name)
    {
        RunOnSta(() =>
        {
            var session = Session(name);
            using var menu = TrayIcon.CreateMenu(new OverlayWindows(session), session);

            // Un FontFamily de GDI+ correcto no basta: ToolStrip dibuja y mide el texto con GDI.
            Assert.Equal(name, GdiFace(menu.Font));
            Assert.Equal(9.75f, menu.Font.SizeInPoints);
            Assert.Equal(GraphicsUnit.Point, menu.Font.Unit);
            var size = Forms.TextRenderer.MeasureText("Ocultar overlay", menu.Font);
            Assert.True(size.Width > 0 && size.Height > 0);
        });
    }

    [Fact]
    public void Opening_refreshes_the_selected_font_and_releases_the_previous_one()
    {
        RunOnSta(() =>
        {
            var session = Session("IBM Plex Sans");
            using var menu = TrayIcon.CreateMenu(new OverlayWindows(session), session);

            foreach (var name in FontChanges)
            {
                var previous = menu.Font;
                session.Update(session.Preferences with { TextFont = name });
                OpenWithoutShowing(menu);

                Assert.NotSame(previous, menu.Font);
                Assert.Equal(name, GdiFace(menu.Font));
                AssertReleased(previous);
                Assert.All(menu.Items.OfType<Forms.ToolStripMenuItem>(), item => Assert.Same(menu.Font, item.Font));
            }

            // Abrirlo de nuevo sin cambiar preferencias conserva la fuente válida.
            var unchanged = menu.Font;
            OpenWithoutShowing(menu);
            Assert.Same(unchanged, menu.Font);
            Assert.Equal("IBM Plex Sans", GdiFace(menu.Font));
        });
    }

    [Fact]
    public void Disposing_the_menu_releases_its_owned_font()
    {
        RunOnSta(() =>
        {
            var session = Session("IBM Plex Sans");
            using var menu = TrayIcon.CreateMenu(new OverlayWindows(session), session);
            var owned = menu.Font;
            Assert.Equal("IBM Plex Sans", GdiFace(owned));

            menu.Dispose();

            Assert.True(menu.IsDisposed);
            AssertReleased(owned);
            menu.Dispose(); // El cierre repetido no intenta usar la fuente ni los recursos liberados.
        });
    }

    private static MeterSession Session(string font) => new(
        new CombatEngine(), new CaptureRecorder(), SkillNames.Empty,
        new MeterPreferences { TextFont = font, LastSeenNews = 1 }, _ => { });

    private static void OpenWithoutShowing(Forms.ContextMenuStrip menu)
    {
        // Dispara el evento real protegido, sin mostrar un popup ni crear un NotifyIcon en el escritorio.
        typeof(Forms.ToolStripDropDown).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(menu, [new CancelEventArgs()]);
    }

    private static void AssertReleased(Font font)
    {
        // GetHeight requiere el objeto nativo: una fuente abandonada sin Dispose seguiría siendo utilizable.
        var error = Record.Exception(() => font.GetHeight(96f));
        Assert.True(error is ArgumentException or ObjectDisposedException, $"La fuente todavía puede medir texto: {error}");
    }

    private static string GdiFace(Font font)
    {
        var dc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, dc);
        var hfont = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            hfont = font.ToHfont();
            previous = NativeMethods.SelectObject(dc, hfont);
            Assert.NotEqual(IntPtr.Zero, previous);
            var face = new char[64];
            var count = NativeMethods.GetTextFace(dc, face.Length, face);
            Assert.True(count > 0);
            return new string(face).TrimEnd('\0');
        }
        finally
        {
            if (previous != IntPtr.Zero)
            {
                NativeMethods.SelectObject(dc, previous);
            }

            if (hfont != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(hfont);
            }

            NativeMethods.DeleteDC(dc);
        }
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // GetResourceStream puede cargar los TTF sin crear App ni ejecutar OnStartup/captura.
                Application.ResourceAssembly ??= typeof(App).Assembly;
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private static class NativeMethods
    {
        [DllImport("gdi32.dll", ExactSpelling = true)]
        public static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        public static extern IntPtr SelectObject(IntPtr dc, IntPtr value);

        [DllImport("gdi32.dll", EntryPoint = "GetTextFaceW", ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern int GetTextFace(IntPtr dc, int count, [Out] char[] face);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr value);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr dc);
    }
}
