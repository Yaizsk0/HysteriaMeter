using System.Windows;
using System.Windows.Input;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.History;

/// <summary>
/// Historial de peleas guardadas, agrupadas por run, con la columna "TU PROGRESO" del boss elegido. Abrir una pelea la
/// muestra en el overlay y la ventana queda abierta, con esa fila marcada. Esc o la X la cierran.
/// </summary>
public partial class HistoryWindow : Window
{
    public HistoryWindow(MeterViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>La cabecera mueve la ventana; un clic en el marco de la búsqueda (la lupa, el borde) le da el foco a la caja.</summary>
    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && (SearchBox == source || SearchBox.IsAncestorOf(source)))
        {
            Search.Focus();
            e.Handled = true;
            return;
        }

        DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
