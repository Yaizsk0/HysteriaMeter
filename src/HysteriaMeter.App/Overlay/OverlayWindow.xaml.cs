using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HysteriaMeter.App.Detail;
using HysteriaMeter.App.History;
using HysteriaMeter.App.FightCard;
using HysteriaMeter.App.Interop;
using HysteriaMeter.App.Themes;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Overlay;

public partial class OverlayWindow : Window, IUiScaled
{
    private const int ToggleClickThroughHotKey = 1;
    private const int ResetHotKey = 2;
    private const int ToggleVisibilityHotKey = 3;
    private const int MarkHotKey = 4;
    private const int MinimizeHotKey = 5;
    private const int PauseHotKey = 6;
    private const int TabHotKey = 7;
    private const double MinZoom = 0.7;
    private const double MaxZoom = 2.0;
    private const double ZoomStep = 0.1;

    private readonly MeterViewModel _viewModel;
    private readonly OverlayWindows? _windows;
    private DetailWindow? _detail;
    private HistoryWindow? _historyWindow;
    private Config.ConfigWindow? _config;
    private FightCardWindow? _fightCard;

    // Límites de la ventana sin la escala de la interfaz (los de OverlayWindow.xaml), y el zoom de Ctrl + rueda. Lo que
    // se ve es zoom × escala; lo guardado (OverlayLayout) va sin la escala, que es de cada PC.
    private readonly Size _minimum;
    private readonly Size _maximum;
    private double _zoom = 1;
    private double _uiScale = 1;

    /// <param name="windows">Gestor de ventanas; <c>null</c> en pruebas, sin atajos globales.</param>
    public OverlayWindow(MeterViewModel viewModel, OverlayLayout? layout = null, OverlayWindows? windows = null)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _windows = windows;
        _minimum = new Size(MinWidth, MinHeight);
        _maximum = new Size(MaxWidth, MaxHeight);
        UiScale.Follow(this);
        RestoreLayout(layout);
        Loaded += (_, _) =>
        {
            _viewModel.ShowsToasts = _windows is null || IsMain;
            _viewModel.Start();
        };
        _viewModel.DetailRequested += ShowDetail;
        _viewModel.RunSummaryRequested += ShowRunSummary;
        _viewModel.CopyImageRequested += CopyImage;
        _viewModel.SaveImageRequested += SaveKillImage;
        _viewModel.PropertyChanged += OnViewModelChanged;
        _viewModel.PropertyChanged += OnSettingsOpenChanged;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _viewModel.IsHistoryOpen = false;
                _viewModel.IsSettingsOpen = false;
                _fightCard?.Close();
            }
        };
        _viewModel.FightCardRequested += ShowFightCard;
        _viewModel.PropertyChanged += OnMinimizedChanged;

        // La tarjeta acompaña al overlay al moverse; el manejador común de visibilidad la cierra al ocultarlo.
        // Esc la cierra primero, porque ella no toma el foco.
        LocationChanged += (_, _) => PlaceFightCard();
        PreviewKeyDown += OnPreviewKeyDown;

        // Una ventana cerrada no puede ser dueña de otra: se suelta del view model al cerrarse.
        Closed += (_, _) =>
        {
            _viewModel.Stop();
            _viewModel.DetailRequested -= ShowDetail;
            _viewModel.RunSummaryRequested -= ShowRunSummary;
            _viewModel.CopyImageRequested -= CopyImage;
            _viewModel.SaveImageRequested -= SaveKillImage;
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel.PropertyChanged -= OnSettingsOpenChanged;
            _config?.Close();
            _viewModel.FightCardRequested -= ShowFightCard;
            _viewModel.PropertyChanged -= OnMinimizedChanged;
        };
    }

    /// <summary>La primera ventana registra los atajos globales (Windows no deja registrar el mismo dos veces).</summary>
    private bool IsMain => _windows is { All.Count: > 0 } w && ReferenceEquals(w.All[0], this);

    /// <summary>Posición y vista actuales; <c>null</c> si la ventana nunca se mostró (no tiene posición).</summary>
    public OverlayLayout? CurrentLayout() =>
        double.IsNaN(Left) || double.IsNaN(Top)
            ? null
            : new OverlayLayout(Left, Top, Width / _uiScale, _zoom, Height / _uiScale, _viewModel.Tab, _viewModel.ShowHealing, _viewModel.IsCompact, _viewModel.IsSessionView, _viewModel.BossOnly, _viewModel.IsRunView);

    /// <summary>Copia el overlay como imagen (para pegar en Discord), al tamaño real en pantalla.</summary>
    private void CopyImage()
    {
        if (RenderOverlay() is not { } bitmap)
        {
            return;
        }

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

    /// <summary>La imagen automática al derrotar un boss: el resultado queda en Configuración, en Exportar.</summary>
    private void SaveKillImage(string path)
    {
        if (SaveImage(path) is { } message)
        {
            _viewModel.ExportMessage = message;
        }
    }

    /// <summary>
    /// Guarda el overlay como .png. Con varias ventanas, solo la primera. Devuelve el resultado ("Imagen guardada: …" o el
    /// error), o <c>null</c> si no guardó nada (no es la principal o todavía no tiene tamaño).
    /// </summary>
    private string? SaveImage(string path)
    {
        if ((_windows is not null && !IsMain) || RenderOverlay() is not { } bitmap)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(path);
            encoder.Save(file);
            _viewModel.ShowSavedImage(path);
            return $"Imagen guardada: {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"No se pudo guardar la imagen: {ex.Message}";
        }
    }

    /// <summary>El overlay dibujado al tamaño real en pantalla; <c>null</c> si todavía no tiene tamaño.</summary>
    private RenderTargetBitmap? RenderOverlay()
    {
        // El PNG comparte las filas: los avisos transitorios nunca tapan el resultado del combate.
        // SetCurrentValue conserva los bindings, que vuelven a su estado original incluso si falla el render.
        var toastVisibility = ToastLayer.Visibility;
        var newsVisibility = NewsCard.Visibility;
        ToastLayer.SetCurrentValue(VisibilityProperty, Visibility.Collapsed);
        NewsCard.SetCurrentValue(VisibilityProperty, Visibility.Collapsed);
        try
        {
            return RenderOverlayContent();
        }
        finally
        {
            ToastLayer.SetCurrentValue(VisibilityProperty, toastVisibility);
            NewsCard.SetCurrentValue(VisibilityProperty, newsVisibility);
            Root.UpdateLayout();
        }
    }

    private RenderTargetBitmap? RenderOverlayContent()
    {
        Root.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(Root.ActualWidth * Zoom.ScaleX * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(Root.ActualHeight * Zoom.ScaleY * dpi.DpiScaleY);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        // VisualBrush dibuja el contenido sin el zoom propio del Grid: se escala a mano al tamaño en pantalla.
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new VisualBrush(Root), null, new Rect(0, 0, Root.ActualWidth * Zoom.ScaleX, Root.ActualHeight * Zoom.ScaleY));
        }

        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }

    private void ShowDetail()
    {
        _fightCard?.Close();
        if (_detail is null)
        {
            _detail = new DetailWindow(_viewModel.Detail) { Owner = this };
            _detail.Closed += (_, _) => _detail = null;
            _detail.Show();
        }

        _detail.Activate();
    }

    /// <summary>
    /// Tarjeta de fin de pelea al derrotar un boss: solo la ventana principal (y si se ve), debajo del overlay o de la
    /// pastilla. Si el overlay deja pasar los clics, la tarjeta también.
    /// </summary>
    private void ShowFightCard(FightCardViewModel card)
    {
        if ((_windows is not null && !IsMain) || !IsVisible)
        {
            card.Detach();
            return;
        }

        _fightCard?.Close();

        // Los dos botones actúan sobre la pelea de la tarjeta: el VM la fija si el overlay muestra otra cosa.
        // ShowDetail (el de esta ventana) cierra la tarjeta.
        card.DetailRequested += () =>
        {
            if (!_viewModel.ShowFightDetail(card.Fight))
            {
                card.SaveMessage = "No estás en esta pelea: no hay detalle tuyo";
            }
        };
        card.SaveRequested += path =>
            card.SaveMessage = _viewModel.SaveFightImage(card.Fight, () => SaveImage(path) ?? "No se pudo dibujar el overlay: probá de nuevo");

        var window = new FightCardWindow(card, _viewModel.IsClickThrough) { Owner = this };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_fightCard, window))
            {
                _fightCard = null;
            }
        };
        _fightCard = window;
        PlaceFightCard();
        window.Show();
        _ = card.LoadAsync();
    }

    /// <summary>Pega la tarjeta de fin de pelea (si hay) debajo de lo que se ve del overlay.</summary>
    private void PlaceFightCard() => _fightCard?.PlaceNear(VisibleBounds(), ScreenWorkArea());

    /// <summary>Al minimizar o abrir el overlay cambia lo que se ve: la tarjeta se reubica cuando terminó el layout.</summary>
    private void OnMinimizedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MeterViewModel.IsMinimized) && _fightCard is not null)
        {
            _ = Dispatcher.InvokeAsync(PlaceFightCard, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>Esc cierra primero la tarjeta de fin de pelea; recién el siguiente llega al Esc del overlay.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _fightCard is { } card)
        {
            card.Close();
            e.Handled = true;
        }
    }

    /// <summary>Lo que se ve del overlay (entero, o solo la pastilla si está minimizado), en coordenadas de pantalla.</summary>
    private Rect VisibleBounds()
    {
        var bounds = Rect.Empty;
        foreach (var child in Root.Children.OfType<FrameworkElement>().Where(c => c.IsVisible))
        {
            bounds.Union(child.TransformToAncestor(this).TransformBounds(new Rect(child.RenderSize)));
        }

        return bounds.IsEmpty
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : new Rect(Left + bounds.X, Top + bounds.Y, bounds.Width, bounds.Height);
    }

    private void ShowRunSummary(DungeonSummaryViewModel summary)
    {
        var window = new Dungeon.DungeonWindow(summary) { Owner = this };
        var placement = Config.ConfigWindow.Placement(
            new Rect(Left, Top, ActualWidth, ActualHeight), ScreenWorkArea(),
            new Size(window.Width, window.Height), new Size(window.MinWidth, window.MinHeight));
        window.Left = placement.Left;
        window.Top = placement.Top;
        window.Width = placement.Width;
        window.Height = placement.Height;
        window.Show();
    }

    /// <summary>El historial es su propia ventana: se abre y se cierra siguiendo IsHistoryOpen.</summary>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MeterViewModel.IsHistoryOpen))
        {
            return;
        }

        if (_viewModel.IsHistoryOpen)
        {
            ShowHistory();
        }
        else
        {
            _historyWindow?.Close();
        }
    }

    private void ShowHistory()
    {
        if (_historyWindow is null)
        {
            _historyWindow = new HistoryWindow(_viewModel) { Owner = this };

            // Cerrarla con la X o con Esc también apaga IsHistoryOpen, así el botón del overlay la vuelve a abrir.
            _historyWindow.Closed += (_, _) =>
            {
                _historyWindow = null;
                _viewModel.IsHistoryOpen = false;
            };
            _historyWindow.Show();
        }

        _historyWindow.Activate();
    }

    /// <summary>El engranaje (ToggleSettings) y Esc abren y cierran la configuración, que va en su propia ventana.</summary>
    private void OnSettingsOpenChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MeterViewModel.IsSettingsOpen))
        {
            return;
        }

        if (!_viewModel.IsSettingsOpen)
        {
            _config?.Close();
            return;
        }

        // Una ventana que nunca se mostró (las de los tests) no puede ser dueña de otra: ahí no se abre nada.
        if (new WindowInteropHelper(this).Handle == IntPtr.Zero)
        {
            return;
        }

        if (_config is null)
        {
            _config = new Config.ConfigWindow(_viewModel, _viewModel.SettingsSection) { Owner = this };
            var placement = Config.ConfigWindow.Placement(
                new Rect(Left, Top, ActualWidth, ActualHeight), ScreenWorkArea(),
                new Size(_config.Width, _config.Height), new Size(_config.MinWidth, _config.MinHeight));
            _config.Left = placement.Left;
            _config.Top = placement.Top;
            _config.Width = placement.Width;
            _config.Height = placement.Height;
            _config.Closed += (_, _) =>
            {
                // Primero se suelta: así IsSettingsOpen = false no vuelve a cerrar una ventana que ya se está cerrando.
                _config = null;
                _viewModel.IsSettingsOpen = false;
            };
            _config.Show();
        }

        _config.Activate();
    }

    private void RestoreLayout(OverlayLayout? layout)
    {
        if (layout is null)
        {
            return;
        }

        Width = Math.Clamp(layout.Width * _uiScale, MinWidth, MaxWidth);
        if (layout.Height > 0)
        {
            Height = Math.Clamp(layout.Height * _uiScale, MinHeight, MaxHeight);
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
        _zoom = Math.Round(Math.Clamp(zoom, MinZoom, MaxZoom), 2);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        Zoom.ScaleX = _zoom * _uiScale;
        Zoom.ScaleY = _zoom * _uiScale;
    }

    /// <summary>
    /// La escala de la interfaz se suma al zoom propio: crecen el contenido, el tamaño y los límites de la ventana en la
    /// misma proporción, y Ctrl + rueda sigue moviendo solo el zoom. Cerca del borde, se corre para no salirse del monitor.
    /// </summary>
    public void ApplyUiScale(double before, double after)
    {
        _uiScale = after;
        MinWidth = _minimum.Width * after;
        MinHeight = _minimum.Height * after;
        MaxWidth = _maximum.Width * after;
        MaxHeight = _maximum.Height * after;
        ApplyZoom();
        Width = Math.Clamp(Width * after / before, MinWidth, MaxWidth);
        Height = Math.Clamp(Height * after / before, MinHeight, MaxHeight);
        UiScale.KeepOnScreen(this);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!IsMain)
        {
            return;
        }

        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        RegisterHotKeys();
        _viewModel.PropertyChanged += OnHotkeysChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnHotkeysChanged;
        foreach (var (id, _) in HotKeys())
        {
            OverlayInterop.UnregisterHotKey(this, id);
        }

        base.OnClosed(e);
    }

    private (int Id, string Text)[] HotKeys() =>
    [
        (ToggleClickThroughHotKey, _viewModel.HotkeyClickThrough),
        (ResetHotKey, _viewModel.HotkeyReset),
        (ToggleVisibilityHotKey, _viewModel.HotkeyVisibility),
        (MarkHotKey, _viewModel.HotkeyMark),
        (MinimizeHotKey, _viewModel.HotkeyMinimize),
        (PauseHotKey, _viewModel.HotkeyPause),
        (TabHotKey, _viewModel.HotkeyTab),
    ];

    private void OnHotkeysChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MeterViewModel.HotkeyClickThrough) or nameof(MeterViewModel.HotkeyReset)
            or nameof(MeterViewModel.HotkeyVisibility) or nameof(MeterViewModel.HotkeyMark)
            or nameof(MeterViewModel.HotkeyMinimize) or nameof(MeterViewModel.HotkeyPause) or nameof(MeterViewModel.HotkeyTab)
            or "Preferences" or "" or null)
        {
            RegisterHotKeys();
        }
    }

    /// <summary>(Re)registra los atajos globales y avisa cuáles ya usa otro programa.</summary>
    private void RegisterHotKeys()
    {
        var taken = new List<string>();
        foreach (var (id, text) in HotKeys())
        {
            OverlayInterop.UnregisterHotKey(this, id);
            // ModifierKeys (Alt=1, Control=2, Shift=4) coincide con los MOD_* de Win32.
            if (HotkeyGesture.Parse(text) is { } gesture && !OverlayInterop.RegisterHotKey(this, id, (uint)gesture.Modifiers, gesture.Key))
            {
                taken.Add(gesture.ToString());
            }
        }

        if (taken.Count > 0)
        {
            _viewModel.HotkeyMessage = $"{string.Join(", ", taken)}: lo usa otro programa, elegí otra combinación";
        }
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
            case MarkHotKey:
                _viewModel.MarkMomentCommand.Execute(null);
                handled = true;
                break;
            case MinimizeHotKey:
                var minimize = !_viewModel.IsMinimized;
                ForAllWindows(w => w._viewModel.IsMinimized = minimize);
                handled = true;
                break;
            case PauseHotKey:
                var pause = !_viewModel.IsPaused;
                ForAllWindows(w => w._viewModel.IsPaused = pause);
                handled = true;
                break;
            case TabHotKey:
                // Solo la ventana principal: las demás suelen ser de una pestaña fija (por ejemplo, una de curación).
                _viewModel.CycleTabCommand.Execute(null);
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

    private void ToggleClickThrough() => SetClickThrough(!_viewModel.IsClickThrough);

    /// <summary>Deja pasar (o no) los clics al juego; la tarjeta de fin de pelea sigue al overlay.</summary>
    public void SetClickThrough(bool enabled)
    {
        _viewModel.IsClickThrough = enabled;
        OverlayInterop.SetClickThrough(this, enabled);
        if (_fightCard is { } card)
        {
            OverlayInterop.SetClickThrough(card, enabled);
        }
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _viewModel.ToggleCompactCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed && _viewModel.CanMoveWindow)
        {
            DragMove();
            SnapToEdges();
        }
    }

    /// <summary>Al soltar la ventana, la pega a un borde de la pantalla o de otra ventana del meter si quedó cerca.</summary>
    private void SnapToEdges()
    {
        if (!_viewModel.SnapWindows || double.IsNaN(Left) || double.IsNaN(Top))
        {
            return;
        }

        var window = new Rect(Left, Top, ActualWidth, ActualHeight);
        var others = _windows?.All.Where(w => !ReferenceEquals(w, this) && w.IsVisible).Select(w => new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight)).ToList() ?? [];
        var snapped = WindowSnap.Snap(window, ScreenWorkArea(), others);
        Left = snapped.X;
        Top = snapped.Y;
    }

    /// <summary>Área de trabajo (sin la barra de tareas) del monitor donde está la ventana, en unidades de WPF.</summary>
    private Rect ScreenWorkArea()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var handle = new WindowInteropHelper(this).Handle;
        var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        return new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY, area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
    }

    private static string? DroppedJson(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files
        && files[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? files[0]
            : null;

    private void OnRootDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedJson(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnRootDrop(object sender, DragEventArgs e)
    {
        if (DroppedJson(e) is { } path)
        {
            _viewModel.OpenSharedFile(path);
            e.Handled = true;
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
            var before = _zoom;
            SetZoom(before + (Math.Sign(e.Delta) * ZoomStep));
            Width = Math.Clamp(Width * _zoom / before, MinWidth, MaxWidth);
            Height = Math.Clamp(Height * _zoom / before, MinHeight, MaxHeight);
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

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        e.Handled = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
