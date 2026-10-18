using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HysteriaMeter.App.Config;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Tests;

// La ventana de configuración en la prueba de humo. Corre dentro de Overlay_and_detail_windows_load_with_rows porque
// necesita la App con Theme.xaml, y WPF admite una sola App por proceso, atada al hilo de ese test.
public sealed partial class WindowSmokeTests
{
    /// <summary>Carga Config/ConfigWindow en cada sección (plantillas, estilos y recursos) y revisa las tarjetas de tema.</summary>
    private static void ConfigWindowLoads(MeterViewModel viewModel)
    {
        foreach (var section in Enum.GetValues<ConfigSection>())
        {
            var window = new ConfigWindow(viewModel, section);
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(window.Width, window.Height));
            content.Arrange(new Rect(0, 0, window.Width, window.Height));
            content.UpdateLayout();
            window.Close();
        }

        // Vista previa de los temas: una tarjeta por tema, con el fondo y el acento de su paleta (todos distintos).
        var cards = viewModel.ThemeCards;
        Assert.Equal(ThemeCard.Ordered(MeterThemes.All).Select(t => t.Name), cards.Select(c => c.Theme.Name));
        Assert.All(cards, card => Assert.NotEqual(Colors.Transparent, ((SolidColorBrush)card.Ground).Color));
        Assert.Equal(cards.Count, cards.Select(card => ((SolidColorBrush)card.Bar).Color).Distinct().Count());

        ConfigRowLimitLabelUpdates(viewModel);
        ConfigWindowFollowsOverlay(viewModel);
    }

    private static void ConfigRowLimitLabelUpdates(MeterViewModel viewModel)
    {
        var window = new ConfigWindow(viewModel);
        try
        {
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(window.Width, window.Height));
            content.Arrange(new Rect(0, 0, window.Width, window.Height));
            content.UpdateLayout();
            var label = (TextBlock)window.FindName("RowLimitLabel");
            viewModel.ShowAllRows = true;
            Assert.EndsWith("todos", label.Text, StringComparison.Ordinal);
            viewModel.ShowAllRows = false;
            viewModel.RowLimit = 12;
            Assert.EndsWith("12", label.Text, StringComparison.Ordinal);
            viewModel.ShowAllRows = true;
            viewModel.ShowAllRows = false;
            Assert.EndsWith("12", label.Text, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    // Se muestra fuera de pantalla, sin gestor de ventanas ni pipeline: prueba Owner y Closed sin captura de red.
    private static void ConfigWindowFollowsOverlay(MeterViewModel viewModel)
    {
        var app = Application.Current;
        var shutdown = app.ShutdownMode;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var overlay = new OverlayWindow(viewModel) { Left = -10000, Top = -10000, ShowActivated = false };
        try
        {
            overlay.Show();
            viewModel.OpenSettingsCommand.Execute(ConfigSection.Captura);
            var window = Assert.Single(overlay.OwnedWindows.OfType<ConfigWindow>());
            Assert.True(((RadioButton)window.FindName("NavCapture")).IsChecked);

            viewModel.OpenSettingsCommand.Execute(ConfigSection.Atajos);
            Assert.True(((RadioButton)window.FindName("NavHotkeys")).IsChecked);
            window.Close();
            Assert.False(viewModel.IsSettingsOpen);

            viewModel.ToggleSettingsCommand.Execute(null);
            Assert.Single(overlay.OwnedWindows.OfType<ConfigWindow>());
            viewModel.EscapeCommand.Execute(null);
            Assert.Empty(overlay.OwnedWindows.OfType<ConfigWindow>());

            viewModel.ToggleSettingsCommand.Execute(null);
            viewModel.IsMinimized = true;
            Assert.False(viewModel.IsSettingsOpen);
            Assert.Empty(overlay.OwnedWindows.OfType<ConfigWindow>());
            viewModel.IsMinimized = false;

            viewModel.ToggleSettingsCommand.Execute(null);
            overlay.Hide();
            Assert.False(viewModel.IsSettingsOpen);
            Assert.Empty(overlay.OwnedWindows.OfType<ConfigWindow>());
        }
        finally
        {
            overlay.Close();
            app.ShutdownMode = shutdown;
        }
    }
}
