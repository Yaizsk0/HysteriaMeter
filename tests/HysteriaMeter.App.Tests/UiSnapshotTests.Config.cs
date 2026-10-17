using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HysteriaMeter.App.Config;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Tests;

// Estados de la ventana de configuración (Config/ConfigWindow): uno por sección de la navegación, contra
// mocks/Config.dc.html (el mock solo dibuja Apariencia).
public sealed partial class UiSnapshotTests
{
    private static readonly State[] ConfigStates =
    [
        new("08-configuracion-1-apariencia", h => Config(h, ConfigSection.Apariencia)),

        // La parte de abajo de Apariencia (fuente, números y ventana), que no entra en los 700 px.
        new("08-configuracion-1b-apariencia-abajo", h =>
        {
            var window = Config(h, ConfigSection.Apariencia);
            window.Loaded += (_, _) => VisiblePage(window)?.ScrollToEnd();
            return window;
        }),
        new("08-configuracion-2-columnas", h => Config(h, ConfigSection.Columnas)),
        new("08-configuracion-3-alertas", h => Config(h, ConfigSection.Alertas)),
        new("08-configuracion-4-atajos", h => Config(h, ConfigSection.Atajos)),

        // Placas de red: las que ve Npcap en esta PC (sin Npcap, solo "Automática" y el aviso).
        new("08-configuracion-5-captura", h => Config(h, ConfigSection.Captura, realDevices: true)),

        // La misma lista con otra placa elegida que la que usa la captura: aparece el aviso de reiniciar. Sin placas
        // reales queda igual que la anterior.
        new("08-configuracion-5b-captura-reiniciar", h =>
        {
            var window = Config(h, ConfigSection.Captura, realDevices: true);
            var vm = (MeterViewModel)window.DataContext;
            vm.SelectedCaptureDevice = vm.CaptureDeviceOptions[^1];
            return window;
        }),

        // Carpeta del juego real de esta PC (%LOCALAPPDATA%\HysteriaMeter\Juego): los números son los que hay en disco.
        new("08-configuracion-6-datos-del-juego", h => Config(h, ConfigSection.DatosDelJuego)),
        new("08-configuracion-7-historial", h => Config(h, ConfigSection.Historial)),
        new("08-configuracion-8-diagnostico", h => Config(h, ConfigSection.Diagnostico)),
    ];

    /// <summary>
    /// La ventana abierta por el camino real (ToggleSettings): así UpdateStatus también llena Diagnostics. El VM no
    /// tiene overlay que la abra, así que la crea el arnés.
    /// </summary>
    private static ConfigWindow Config(Harness h, ConfigSection section, bool realDevices = false)
    {
        var vm = new MeterViewModel(h.Session());
        vm.ToggleSettingsCommand.Execute(null);
        if (realDevices)
        {
            // Solo lista las placas (no abre ninguna). Se leen acá y la fuente vuelve a la de los tests.
            var previous = CaptureDevices.Source;
            CaptureDevices.Source = CaptureDevices.FromNpcap;
            _ = vm.CaptureDeviceOptions;
            CaptureDevices.Source = previous;
        }

        return new ConfigWindow(Refreshed(vm), section);
    }

    /// <summary>El ScrollViewer de la sección que se ve (cada sección es uno, y las otras están ocultas).</summary>
    private static ScrollViewer? VisiblePage(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer { IsVisible: true } page)
            {
                return page;
            }

            if (VisiblePage(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
