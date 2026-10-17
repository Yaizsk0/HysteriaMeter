using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace HysteriaMeter.App.Welcome;

/// <summary>
/// Bienvenida del primer arranque, centrada en pantalla. Mientras está abierta, cada segundo vuelve a mirar el estado
/// de la captura (en memoria: CaptureStatus) para pasar "Esperando tráfico" a "Listo" cuando llega el juego.
/// </summary>
public partial class WelcomeWindow : Window
{
    private readonly WelcomeViewModel _viewModel;
    private readonly DispatcherTimer _timer;

    public WelcomeWindow(WelcomeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => _viewModel.Refresh(), Dispatcher);
        viewModel.CloseRequested += Close;
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            viewModel.CloseRequested -= Close;
        };
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}
