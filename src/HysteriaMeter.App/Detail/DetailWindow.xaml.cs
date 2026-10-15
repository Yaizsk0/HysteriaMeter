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

    private void ShowLog(LogViewModel log) => new Log.LogWindow(log) { Owner = this }.Show();

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
