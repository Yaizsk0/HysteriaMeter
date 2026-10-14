using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using HysteriaMeter.App.Interop;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Overlay;

public partial class OverlayWindow : Window
{
    private const int ToggleClickThroughHotKey = 1;

    private readonly MeterViewModel _viewModel;

    public OverlayWindow(MeterViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Loaded += (_, _) => _viewModel.Start();
        Closed += (_, _) => _viewModel.Stop();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        OverlayInterop.RegisterHotKey(this, ToggleClickThroughHotKey, OverlayInterop.ModControl | OverlayInterop.ModShift, Key.L);
    }

    protected override void OnClosed(EventArgs e)
    {
        OverlayInterop.UnregisterHotKey(this, ToggleClickThroughHotKey);
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == OverlayInterop.HotKeyMessage && wParam.ToInt32() == ToggleClickThroughHotKey)
        {
            ToggleClickThrough();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ToggleClickThrough()
    {
        _viewModel.IsClickThrough = !_viewModel.IsClickThrough;
        OverlayInterop.SetClickThrough(this, _viewModel.IsClickThrough);
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnLockClick(object sender, RoutedEventArgs e) => ToggleClickThrough();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
