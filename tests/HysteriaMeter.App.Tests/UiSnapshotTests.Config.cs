using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HysteriaMeter.App.Config;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Events;

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
        new("08-configuracion-1c-apariencia-limite", h =>
        {
            var window = Config(h, ConfigSection.Apariencia);
            var vm = (MeterViewModel)window.DataContext;
            vm.ShowAllRows = false;
            vm.RowLimit = 12;
            return window;
        }),
        new("08-configuracion-2-columnas", h => Config(h, ConfigSection.Columnas)),
        new("08-configuracion-3-alertas", h => Config(h, ConfigSection.Alertas)),
        new("08-configuracion-4-atajos", h => Config(h, ConfigSection.Atajos)),
        new("08-configuracion-4b-atajos-minimo", h =>
        {
            var window = Config(h, ConfigSection.Atajos);
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            return window;
        }),

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

        // Con una pelea pasada por el motor: el boss y un add cierran y el otro add no (VerifiedSession).
        new("08-configuracion-8-diagnostico", h => Config(h, ConfigSection.Diagnostico, session: VerifiedSession(h))),

        // La misma con el tema "Mínimo" (no hay tema claro: los cuatro son oscuros), puesto en los recursos de la ventana
        // como lo pone App.ApplyTheme en los de la aplicación: lo que sigue al tema va por DynamicResource.
        new("08-configuracion-8b-diagnostico-minimo", h =>
        {
            var window = Config(h, ConfigSection.Diagnostico, session: VerifiedSession(h));
            MeterThemes.Apply(window.Resources, "Mínimo", MeterThemes.Snapshot(Application.Current.Resources));
            return window;
        }),
    ];

    /// <summary>
    /// La ventana abierta por el camino real (ToggleSettings): así UpdateStatus también llena Diagnostics. El VM no
    /// tiene overlay que la abra, así que la crea el arnés.
    /// </summary>
    private static ConfigWindow Config(Harness h, ConfigSection section, bool realDevices = false, MeterSession? session = null)
    {
        var vm = new MeterViewModel(session ?? h.Session());
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

    /// <summary>
    /// Una pelea en curso con tres objetivos verificados, de mayor a menor daño contado: el boss (con nombre en la tabla
    /// de NPCs) pierde 40.000 con 40.000 contados y cierra; un add pierde 14.000 con 10.000 contados y no cierra (faltan
    /// 4.000); el otro muere con 3.500 contados contra 3.000 de vida y cierra con 500 de overkill.
    /// </summary>
    private static MeterSession VerifiedSession(Harness h)
    {
        var session = h.Session();
        var engine = session.Engine;
        engine.Entities.Npcs = new NpcCatalog(new Dictionary<uint, NpcInfo> { [500] = new("Boss de prueba", null, IsBoss: true) });
        var start = Base;
        engine.Apply(new LocalPlayerEvent(start, 1, "Prueba"));
        engine.Apply(new EntityInfoEvent(start, 1, "Prueba", 11));
        engine.Apply(new EntitySpawnEvent(start, 900, null, 500, Hp: 100_000, MaxHp: 100_000));
        engine.Apply(new EntitySpawnEvent(start, 901, null, 2_400_456, Hp: 20_000, MaxHp: 20_000));
        engine.Apply(new EntitySpawnEvent(start, 902, null, 2_400_789, Hp: 3_000, MaxHp: 3_000));
        for (var second = 0; second < 40; second++)
        {
            engine.Apply(new DamageEvent(start.AddSeconds(second), 1, 900, 11_010_000, 1_000, HitModifiers.Critical));
        }

        engine.Apply(new HealthEvent(start.AddSeconds(39), 900, 60_000));
        for (var second = 40; second < 45; second++)
        {
            engine.Apply(new DamageEvent(start.AddSeconds(second), 1, 901, 11_010_000, 2_000, HitModifiers.None));
        }

        engine.Apply(new HealthEvent(start.AddSeconds(44), 901, 6_000));
        engine.Apply(new DamageEvent(start.AddSeconds(45), 1, 902, 11_010_000, 1_000, HitModifiers.None));
        engine.Apply(new DamageEvent(start.AddSeconds(46), 1, 902, 11_010_000, 1_000, HitModifiers.None));
        engine.Apply(new DamageEvent(start.AddSeconds(47), 1, 902, 11_010_000, 1_500, HitModifiers.None));
        engine.Apply(new DeathEvent(start.AddSeconds(47), 902));
        return session;
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
