using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Config;

/// <summary>Secciones de la navegación lateral de la configuración, en el orden en que se ven.</summary>
public enum ConfigSection
{
    Apariencia,
    Columnas,
    Alertas,
    Atajos,
    Captura,
    DatosDelJuego,
    Historial,
    Diagnostico,
}

/// <summary>
/// Configuración en ventana propia (reemplaza al panel que tapaba las filas del overlay). Edita el mismo view model de
/// la ventana que la abrió: las opciones "solo esta ventana" son de esa ventana, el resto se comparte entre todas.
/// </summary>
public partial class ConfigWindow : Window
{
    private readonly MeterViewModel _viewModel;

    public ConfigWindow(MeterViewModel viewModel, ConfigSection section = ConfigSection.Apariencia)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Navigation(section).IsChecked = true;
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

    /// <summary>Esc cierra, salvo que la tecla la haya usado un control (una caja de atajo esperando la combinación).</summary>
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
