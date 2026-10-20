using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using Forms = System.Windows.Forms;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// El menú de la bandeja es de WinForms y no ve el LayoutTransform de la escala de la interfaz: agranda su fuente y sus
/// medidas con la preferencia, al crearlo y cada vez que se abre. Sin mostrar el menú ni crear un NotifyIcon.
/// </summary>
[Collection("Overlay WPF")]
public sealed class TrayScaleTests
{
    private static readonly double[] ScaleChanges = [1.5, 1.25, 1.0];

    [Fact]
    public void Menu_is_created_at_the_saved_scale()
    {
        RunOnSta(() =>
        {
            var session = Session(1.5);
            using var menu = TrayIcon.CreateMenu(new OverlayWindows(session), session);

            Assert.Equal(14.625f, menu.Font.SizeInPoints);
            Assert.Equal(GraphicsUnit.Point, menu.Font.Unit);
            AssertMeasures(menu, 1.5);
        });
    }

    [Fact]
    public void Opening_applies_the_new_scale_and_releases_the_previous_font()
    {
        RunOnSta(() =>
        {
            var session = Session(1.0);
            using var menu = TrayIcon.CreateMenu(new OverlayWindows(session), session);
            Assert.Equal(9.75f, menu.Font.SizeInPoints);
            AssertMeasures(menu, 1.0);
            var height = menu.GetPreferredSize(System.Drawing.Size.Empty).Height;

            foreach (var scale in ScaleChanges)
            {
                var previous = menu.Font;
                session.Update(session.Preferences with { UiScale = scale });
                OpenWithoutShowing(menu);

                Assert.NotSame(previous, menu.Font);
                AssertReleased(previous);
                Assert.Equal((float)(9.75 * scale), menu.Font.SizeInPoints);
                Assert.All(menu.Items.OfType<Forms.ToolStripMenuItem>(), item => Assert.Same(menu.Font, item.Font));
                AssertMeasures(menu, scale);

                // El alto crece con el texto y el relleno: al 150 % el menú entero mide cerca de 1,5 veces.
                var ratio = menu.GetPreferredSize(System.Drawing.Size.Empty).Height / (double)height;
                Assert.InRange(ratio, scale - 0.1, scale + 0.1);
            }

            // Abrirlo de nuevo con la misma escala conserva la fuente.
            var unchanged = menu.Font;
            OpenWithoutShowing(menu);
            Assert.Same(unchanged, menu.Font);
        });
    }

    [Fact]
    public void Scale_out_of_range_stays_at_one()
    {
        RunOnSta(() =>
        {
            var session = Session(5);
            using var menu = TrayIcon.CreateMenu(new OverlayWindows(session), session);

            Assert.Equal(9.75f, menu.Font.SizeInPoints);
            AssertMeasures(menu, 1.0);
        });
    }

    /// <summary>Las medidas del mock (px a 96 ppp) por el DPI del menú y la escala, en el menú y en su renderer.</summary>
    private static void AssertMeasures(Forms.ContextMenuStrip menu, double scale)
    {
        var dpi = menu.DeviceDpi / 96.0;
        int Px(double value) => (int)Math.Round(value * dpi * scale);

        Assert.Equal(scale, Assert.IsType<TrayMenuRenderer>(menu.Renderer).UiScale);
        Assert.Equal(Px(220), menu.MinimumSize.Width);
        Assert.Equal(Px(9), Assert.Single(menu.Items.OfType<Forms.ToolStripSeparator>()).Height);
        Assert.All(menu.Items.OfType<Forms.ToolStripMenuItem>(), item =>
        {
            Assert.Equal(Px(7), item.Padding.Top);
            Assert.Equal(Px(7), item.Padding.Bottom);
            Assert.Equal(Px(4), item.Padding.Left);
        });

        var edge = (int)Math.Round(((TrayMenuRenderer.Inset * scale) - 2) * dpi);
        Assert.Equal(edge, menu.Items[0].Margin.Top);
        Assert.Equal(edge, menu.Items[^1].Margin.Bottom);
    }

    private static MeterSession Session(double scale) => new(
        new CombatEngine(), new CaptureRecorder(), SkillNames.Empty,
        new MeterPreferences { TextFont = "IBM Plex Sans", UiScale = scale, LastSeenNews = 1 }, _ => { });

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
}
