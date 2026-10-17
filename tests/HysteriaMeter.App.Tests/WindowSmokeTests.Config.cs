using System.Windows;
using System.Windows.Media;
using HysteriaMeter.App.Config;
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
        Assert.Equal(MeterThemes.All.Select(t => t.Name), cards.Select(c => c.Theme.Name));
        Assert.All(cards, card => Assert.NotEqual(Colors.Transparent, ((SolidColorBrush)card.Ground).Color));
        Assert.Equal(cards.Count, cards.Select(card => ((SolidColorBrush)card.Bar).Color).Distinct().Count());
    }
}
