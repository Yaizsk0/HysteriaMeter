using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using HysteriaMeter.App.Themes;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Config;

/// <summary>
/// Configuración en ventana propia (reemplaza al panel que tapaba las filas del overlay). Edita el mismo view model de
/// la ventana que la abrió: las opciones "solo esta ventana" son de esa ventana, el resto se comparte entre todas.
/// </summary>
public partial class ConfigWindow : Window
{
    /// <summary>Ancho de una tarjeta de atajo con su nombre y su tecla sin cortar (más los 10 px de separación).</summary>
    private const double HotkeyCardWidth = 245;

    private readonly MeterViewModel _viewModel;

    public ConfigWindow(MeterViewModel viewModel, ConfigSection section = ConfigSection.Apariencia)
    {
        _viewModel = viewModel;
        InitializeComponent();
        UiScale.Follow(this);
        DataContext = viewModel;
        Navigation(section).IsChecked = true;

        // Abierta, salta a la sección que pida el view model (OpenSettings, por ejemplo "Elegir placa de red").
        viewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= OnViewModelChanged;
    }

    /// <summary>
    /// Dónde se abre: centrada en el área de trabajo del monitor del overlay y achicada para que entre entera, sin bajar
    /// del tamaño mínimo (sin barra de título, una ventana que se pasa de la pantalla no se puede reacomodar). Si el
    /// overlay no está en esa pantalla (las pruebas lo dibujan fuera), centrada sobre él.
    /// </summary>
    public static Rect Placement(Rect owner, Rect workArea, Size size, Size minimum)
    {
        if (!workArea.IntersectsWith(owner))
        {
            return new Rect(owner.X + ((owner.Width - size.Width) / 2), owner.Y + ((owner.Height - size.Height) / 2), size.Width, size.Height);
        }

        var width = Math.Max(minimum.Width, Math.Min(size.Width, workArea.Width));
        var height = Math.Max(minimum.Height, Math.Min(size.Height, workArea.Height));
        return new Rect(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2), workArea.Y + Math.Max(0, (workArea.Height - height) / 2), width, height);
    }

    private RadioButton Navigation(ConfigSection section) => section switch
    {
        ConfigSection.Columnas => NavColumns,
        ConfigSection.Alertas => NavAlerts,
        ConfigSection.Atajos => NavHotkeys,
        ConfigSection.Captura => NavCapture,
        ConfigSection.DatosDelJuego => NavGameData,
        ConfigSection.Historial => NavHistory,
        ConfigSection.Diagnostico => NavDiagnostics,
        _ => NavAppearance,
    };

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MeterViewModel.SettingsSection))
        {
            Navigation(_viewModel.SettingsSection).IsChecked = true;
        }
    }

    /// <summary>
    /// Esc cierra, salvo que la tecla la haya usado un control (el ComboBox desplegado la usa para cerrarse). En una caja
    /// de atajo también cierra: Esc no sirve como atajo y la caja lo deja pasar.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Arrastra desde el fondo de la navegación; los clics en las secciones los toman los RadioButton.
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Grillas de atajos: tres columnas como el mock si entran sin cortar los nombres; si la ventana se achica, dos (o una).
    /// </summary>
    private void OnHotkeyGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is UniformGrid grid && e.WidthChanged)
        {
            grid.Columns = Math.Clamp((int)(e.NewSize.Width / HotkeyCardWidth), 1, 3);
        }
    }

    /// <summary>
    /// Captura la combinación nueva en una caja de atajo: con Borrar o Supr (sin modificadores) se saca el atajo. La
    /// validación (que sea un atajo posible y no esté repetido) la hace el view model.
    /// </summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape)
        {
            // Tab mueve el foco y Esc cierra la ventana, como en cualquier otro control.
            return;
        }

        e.Handled = true;
        string? text = key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None
            ? string.Empty
            : HotkeyGesture.From(Keyboard.Modifiers, key)?.ToString();
        if (text is null)
        {
            // Todavía solo hay modificadores (Ctrl, Shift...): se espera la tecla.
            return;
        }

        box.Text = text;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    /// <summary>"Abrir pelea…": la configuración queda abierta y el overlay muestra la pelea elegida.</summary>
    private void OnOpenFightClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(MeterViewModel.ExportFolder);
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Abrir pelea",
            Filter = "Pelea de Hysteria Meter (*.json)|*.json",
            InitialDirectory = MeterViewModel.ExportFolder,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.OpenSharedFile(dialog.FileName);
        }
    }
}
