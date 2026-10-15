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
}
