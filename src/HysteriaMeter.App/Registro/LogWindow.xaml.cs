using System.Windows;
using System.Windows.Input;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Registro;

/// <summary>Registro de la pelea en curso o de la última que terminó.</summary>
public partial class LogWindow : Window
{
    public LogWindow(LogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>Esc cierra la ventana (si una caja de texto no lo usó antes, como al renombrar una skill).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close();
        }
    }
}
