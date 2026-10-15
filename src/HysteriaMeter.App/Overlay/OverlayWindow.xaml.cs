using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using HysteriaMeter.App.Detail;
using HysteriaMeter.App.Interop;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Overlay;

public partial class OverlayWindow : Window
{
    private const int ToggleClickThroughHotKey = 1;
    private const double MinZoom = 0.7;
    private const double MaxZoom = 2.0;
    private const double ZoomStep = 0.1;

    private readonly MeterViewModel _viewModel;
    private DetailWindow? _detail;

    public OverlayWindow(MeterViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        RestoreLayout();
        Loaded += (_, _) => _viewModel.Start();
        Closing += (_, _) => SaveLayout();
        Closed += (_, _) => _viewModel.Stop();
        _viewModel.DetailRequested += ShowDetail;
    }

    private void ShowDetail()
    {
        if (_detail is null)
        {
            _detail = new DetailWindow(_viewModel.Detail) { Owner = this };
            _detail.Closed += (_, _) => _detail = null;
            _detail.Show();
        }

        _detail.Activate();
    }

    private void SaveLayout()
    {
        // Una ventana que nunca se mostró no tiene posición (NaN), y JSON no admite NaN.
        if (double.IsNaN(Left) || double.IsNaN(Top))
        {
            return;
        }

        OverlayLayout.Save(new OverlayLayout(Left, Top, Width, Zoom.ScaleX, Height));
    }

    private void RestoreLayout()
    {
        if (OverlayLayout.Load() is not { } layout)
        {
            return;
        }

        Width = Math.Clamp(layout.Width, MinWidth, MaxWidth);
        if (layout.Height > 0)
        {
            Height = Math.Clamp(layout.Height, MinHeight, MaxHeight);
        }

        SetZoom(layout.Zoom);

        // Si la posición guardada quedó fuera de pantalla (otro monitor), se usa la de Windows.
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (screen.Contains(new Point(layout.Left + 40, layout.Top + 10)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = layout.Left;
            Top = layout.Top;
        }
    }

    private void SetZoom(double zoom)
    {
        zoom = Math.Round(Math.Clamp(zoom, MinZoom, MaxZoom), 2);
        Zoom.ScaleX = zoom;
        Zoom.ScaleY = zoom;
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

    private void OnResizeDragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Clamp(Width + (e.HorizontalChange * Zoom.ScaleX), MinWidth, MaxWidth);
        Height = Math.Clamp(Height + (e.VerticalChange * Zoom.ScaleY), MinHeight, MaxHeight);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // El tamaño acompaña al zoom para que las columnas no se aprieten.
            var before = Zoom.ScaleX;
            SetZoom(before + (Math.Sign(e.Delta) * ZoomStep));
            Width = Math.Clamp(Width * Zoom.ScaleX / before, MinWidth, MaxWidth);
            Height = Math.Clamp(Height * Zoom.ScaleY / before, MinHeight, MaxHeight);
            e.Handled = true;
        }
    }

    /// <summary>Clic en una fila: desglose por skill de ese jugador en otra ventana.</summary>
    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActorRowViewModel row })
        {
            _viewModel.ShowDetailCommand.Execute(row);
            e.Handled = true;
        }
    }

    private void OnLockClick(object sender, RoutedEventArgs e) => ToggleClickThrough();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
