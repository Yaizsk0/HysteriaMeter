// Opt-in y aislado: HYSTERIA_TRAY_SNAPSHOT=C:\ruta\tray.png
// dotnet test tests/HysteriaMeter.App.Tests --filter FullyQualifiedName~TraySnapshotTests
// No combinar con UiSnapshotTests en el mismo proceso: WPF solo admite una App.

using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using Forms = System.Windows.Forms;

namespace HysteriaMeter.App.Tests;

/// <summary>Captura el menú real con sus recursos de tema, sin mostrarlo ni crear un NotifyIcon.</summary>
public sealed class TraySnapshotTests
{
    [Fact]
    public void Generate_tray_snapshot()
    {
        var path = Environment.GetEnvironmentVariable("HYSTERIA_TRAY_SNAPSHOT");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            try
            {
                if (Application.Current is not null)
                {
                    throw new InvalidOperationException("Ejecutar TraySnapshotTests solo, en un proceso sin otra App WPF.");
                }

                app = new App();
                app.InitializeComponent(); // Sin Run/OnStartup: no host, captura, preferencias del usuario ni bandeja.
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var session = new MeterSession(
                    new CombatEngine(), new CaptureRecorder(), SkillNames.Empty,
                    new MeterPreferences { TextFont = "IBM Plex Sans", LastSeenNews = 1 }, _ => { });
                using var menu = TrayIcon.CreateMenu(new OverlayWindows(session), session);

                // Prepara textos, atajos, colores y fuente por el evento real sin abrir un popup.
                typeof(Forms.ToolStripDropDown).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(menu, [new CancelEventArgs()]);
                menu.Size = menu.GetPreferredSize(System.Drawing.Size.Empty);
                menu.PerformLayout();
                _ = menu.Handle; // Handle oculto, necesario para WM_PRINT/DrawToBitmap; nunca Show().
                using var bitmap = new Bitmap(menu.Width, menu.Height, PixelFormat.Format32bppArgb);
                bitmap.SetResolution(96, 96);
                menu.DrawToBitmap(bitmap, new Rectangle(System.Drawing.Point.Empty, menu.Size));
                bitmap.Save(path, ImageFormat.Png);
                Assert.False(menu.Visible);
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                try
                {
                    app?.Shutdown();
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.SystemIdle);
                    Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    failure ??= ExceptionDispatchInfo.Capture(ex);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
