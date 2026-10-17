using System.Windows;
using System.Windows.Threading;
using HysteriaMeter.App.Interop;

namespace HysteriaMeter.App.FightCard;

/// <summary>
/// Tarjeta de fin de pelea: ventana chica sin bordes, siempre encima y sin robar el foco, pegada debajo del overlay (o de
/// la pastilla). Se cierra sola a los 12 s (no cuenta mientras el mouse está encima), al empezar otra pelea, con Esc o ✕.
/// </summary>
public partial class FightCardWindow : Window
{
    /// <summary>Cuánto queda en pantalla.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(12);

    /// <summary>Separación con el overlay.</summary>
    private const double Gap = 6;

    private readonly FightCardViewModel _viewModel;
    private readonly bool _clickThrough;
    private readonly DispatcherTimer _timer;
    private TimeSpan _left = Lifetime;
    private Rect? _anchor;
    private Rect _workArea;

    /// <param name="clickThrough">El overlay deja pasar los clics al juego: la tarjeta también.</param>
    public FightCardWindow(FightCardViewModel viewModel, bool clickThrough = false)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _clickThrough = clickThrough;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher);
        viewModel.CloseRequested += Close;
        SourceInitialized += (_, _) =>
        {
            if (_clickThrough)
            {
                OverlayInterop.SetClickThrough(this, true);
            }
        };
        Loaded += (_, _) => _timer.Start();
        SizeChanged += (_, _) => Place();
        Closed += (_, _) =>
        {
            _timer.Stop();
            viewModel.CloseRequested -= Close;
            viewModel.Detach();
        };
    }

    /// <summary>
    /// Debajo de <paramref name="anchor"/> (lo que se ve del overlay, en coordenadas de pantalla de WPF) y alineada a su
    /// izquierda; si no entra abajo en <paramref name="workArea"/>, arriba. Se recalcula cuando la tarjeta cambia de alto.
    /// </summary>
    public void PlaceNear(Rect anchor, Rect workArea)
    {
        _anchor = anchor;
        _workArea = workArea;
        Place();
    }

    /// <summary>Dónde va la esquina de la tarjeta de tamaño <paramref name="card"/> junto a <paramref name="anchor"/>.</summary>
    public static Point Placement(Rect anchor, Size card, Rect workArea, double gap = Gap)
    {
        var left = Math.Clamp(anchor.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - card.Width));
        var top = anchor.Bottom + gap;
        if (top + card.Height > workArea.Bottom)
        {
            top = anchor.Top - gap - card.Height;
        }

        return new Point(left, Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - card.Height)));
    }

    private void Place()
    {
        if (_anchor is not { } anchor)
        {
            return;
        }

        var size = new Size(double.IsNaN(Width) ? ActualWidth : Width, ActualHeight);
        var at = Placement(anchor, size, _workArea);
        Left = at.X;
        Top = at.Y;
    }

    private void Tick()
    {
        if (!IsMouseOver)
        {
            _left -= _timer.Interval;
        }

        if (_left <= TimeSpan.Zero || _viewModel.IsStale())
        {
            Close();
        }
    }
}
