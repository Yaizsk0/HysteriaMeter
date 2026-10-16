using System.Windows;
using System.Windows.Input;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Detail;

/// <summary>Desglose por skill del jugador elegido en el overlay. Una sola ventana que cambia de jugador.</summary>
public partial class DetailWindow : Window
{
    public DetailWindow(ActorDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.LogRequested += ShowLog;
        Closed += (_, _) => viewModel.LogRequested -= ShowLog;
    }

    private void ShowLog(LogViewModel log) => new Registro.LogWindow(log) { Owner = this }.Show();

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
