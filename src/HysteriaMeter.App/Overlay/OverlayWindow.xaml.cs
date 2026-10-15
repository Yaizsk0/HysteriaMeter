using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HysteriaMeter.App.Detail;
using HysteriaMeter.App.Interop;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Overlay;

public partial class OverlayWindow : Window
{
    private const int ToggleClickThroughHotKey = 1;
    private const int ResetHotKey = 2;
    private const int ToggleVisibilityHotKey = 3;
    private const double MinZoom = 0.7;
    private const double MaxZoom = 2.0;
    private const double ZoomStep = 0.1;

    private readonly MeterViewModel _viewModel;
    private readonly OverlayWindows? _windows;
    private DetailWindow? _detail;

    /// <param name="windows">Gestor de ventanas; <c>null</c> en pruebas, sin atajos globales.</param>
    public OverlayWindow(MeterViewModel viewModel, OverlayLayout? layout = null, OverlayWindows? windows = null)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _windows = windows;
        RestoreLayout(layout);
        Loaded += (_, _) => _viewModel.Start();
        _viewModel.DetailRequested += ShowDetail;
        _viewModel.CopyImageRequested += CopyImage;

        // Una ventana cerrada no puede ser dueña de otra: se suelta del view model al cerrarse.
        Closed += (_, _) =>
        {
            _viewModel.Stop();
            _viewModel.DetailRequested -= ShowDetail;
            _viewModel.CopyImageRequested -= CopyImage;
        };
    }

    /// <summary>La primera ventana registra los atajos globales (Windows no deja registrar el mismo dos veces).</summary>
    private bool IsMain => _windows is { All.Count: > 0 } w && ReferenceEquals(w.All[0], this);

    /// <summary>Posición y vista actuales; <c>null</c> si la ventana nunca se mostró (no tiene posición).</summary>
    public OverlayLayout? CurrentLayout() =>
        double.IsNaN(Left) || double.IsNaN(Top)
            ? null
            : new OverlayLayout(Left, Top, Width, Zoom.ScaleX, Height, _viewModel.Tab, _viewModel.ShowHealing, _viewModel.IsCompact, _viewModel.IsSessionView, _viewModel.BossOnly);

    /// <summary>Copia el overlay como imagen (para pegar en Discord), al tamaño real en pantalla.</summary>
    private void CopyImage()
    {
        Root.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(Root.ActualWidth * Zoom.ScaleX * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(Root.ActualHeight * Zoom.ScaleY * dpi.DpiScaleY);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // VisualBrush dibuja el contenido sin el zoom propio del Grid: se escala a mano al tamaño en pantalla.
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new VisualBrush(Root), null, new Rect(0, 0, Root.ActualWidth * Zoom.ScaleX, Root.ActualHeight * Zoom.ScaleY));
        }

        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        try
        {
            Clipboard.SetImage(bitmap);
            _viewModel.ExportMessage = "Imagen copiada: pegala en Discord con Ctrl+V";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            _viewModel.ExportMessage = "Otro programa tiene el portapapeles abierto; probá de nuevo";
        }
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

    private void RestoreLayout(OverlayLayout? layout)
    {
        if (layout is null)
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
        if (!IsMain)
        {
            return;
        }

        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        OverlayInterop.RegisterHotKey(this, ToggleClickThroughHotKey, OverlayInterop.ModControl | OverlayInterop.ModShift, Key.L);
        OverlayInterop.RegisterHotKey(this, ResetHotKey, OverlayInterop.ModControl | OverlayInterop.ModShift, Key.R);
        OverlayInterop.RegisterHotKey(this, ToggleVisibilityHotKey, OverlayInterop.ModControl | OverlayInterop.ModShift, Key.H);
    }

    protected override void OnClosed(EventArgs e)
    {
        OverlayInterop.UnregisterHotKey(this, ToggleClickThroughHotKey);
        OverlayInterop.UnregisterHotKey(this, ResetHotKey);
        OverlayInterop.UnregisterHotKey(this, ToggleVisibilityHotKey);
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != OverlayInterop.HotKeyMessage)
        {
            return IntPtr.Zero;
        }

        switch (wParam.ToInt32())
        {
            case ToggleClickThroughHotKey:
                ForAllWindows(w => w.ToggleClickThrough());
                handled = true;
                break;
            case ResetHotKey:
                _viewModel.ResetCommand.Execute(null);
                handled = true;
                break;
            case ToggleVisibilityHotKey:
                // Hide conserva la ventana nativa, así que el atajo sigue llegando para volver a mostrarla.
                var show = !IsVisible;
                ForAllWindows(w => w.Visibility = show ? Visibility.Visible : Visibility.Hidden);
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void ForAllWindows(Action<OverlayWindow> action)
    {
        if (_windows is null)
        {
            action(this);
            return;
        }

        _windows.ForEach(action);
    }

    private void ToggleClickThrough()
    {
        _viewModel.IsClickThrough = !_viewModel.IsClickThrough;
        OverlayInterop.SetClickThrough(this, _viewModel.IsClickThrough);
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _viewModel.ToggleCompactCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnNoteLostFocus(object sender, RoutedEventArgs e) => _viewModel.SaveNoteCommand.Execute(null);

    private void OnNoteKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.SaveNoteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnMouseEnterWindow(object sender, MouseEventArgs e) => _viewModel.IsHovered = true;

    private void OnMouseLeaveWindow(object sender, MouseEventArgs e) => _viewModel.IsHovered = false;

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

    private void OnLockClick(object sender, RoutedEventArgs e) => ForAllWindows(w => w.ToggleClickThrough());

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
