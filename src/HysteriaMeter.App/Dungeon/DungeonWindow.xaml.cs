using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HysteriaMeter.App.Themes;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Dungeon;

/// <summary>Modo dungeon: resumen de una run con cada boss, sus intentos y las muertes de cada jugador.</summary>
public partial class DungeonWindow : Window
{
    private readonly DungeonSummaryViewModel _viewModel;

    public DungeonWindow(DungeonSummaryViewModel viewModel)
    {
        InitializeComponent();
        UiScale.Follow(this);
        _viewModel = viewModel;
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

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_viewModel.ShareText);
            CopyButton.Content = "Copiado ✓";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Otro programa tiene el portapapeles tomado: se puede reintentar.
            CopyButton.Content = "No se pudo copiar, reintentá";
        }
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var (path, error) = _viewModel.Export();
        ExportButton.Content = path is not null ? "Guardado ✓" : "No se pudo guardar, reintentá";
        ExportButton.ToolTip = path is not null ? $"Guardado en {path}" : error;
    }

    /// <summary>Un clic en una celda de la matriz abre ese intento en el overlay; esta ventana queda abierta.</summary>
    private void OnCellClick(object sender, RoutedEventArgs e) =>
        _viewModel.OpenFight((sender as FrameworkElement)?.DataContext as PlayerBossCell);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}

/// <summary>Escala posiciones y anchos del timeline, sin estirar la tipografía de sus etiquetas.</summary>
public sealed class DungeonTimelinePanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var availableWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : DungeonSummaryViewModel.TimelineWidth;
        foreach (UIElement child in InternalChildren)
        {
            var width = child is ContentPresenter { Content: SegmentItem segment }
                ? Math.Max(segment.Width / DungeonSummaryViewModel.TimelineWidth * availableWidth - 2, 0)
                : 0;
            child.Measure(new Size(width, 34));
        }

        return new Size(0, 34);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            if (child is ContentPresenter { Content: SegmentItem segment })
            {
                var left = Math.Clamp(segment.Left / DungeonSummaryViewModel.TimelineWidth * finalSize.Width, 0, finalSize.Width);
                var width = Math.Min(Math.Max(segment.Width / DungeonSummaryViewModel.TimelineWidth * finalSize.Width - 2, 0), finalSize.Width - left);
                child.Arrange(new Rect(left, 0, width, finalSize.Height));
            }
        }

        return finalSize;
    }
}

/// <summary>
/// Pone el nombre de un tramo del timeline en su <see cref="TextBlock"/> hijo. Un nombre se recorta por el final ("Krom…",
/// con la TextTrimming del hijo); con <see cref="FromStart"/>, por el principio ("…400456"): el número de una plantilla
/// sin nombre se distingue por el final (los bosses de una dungeon suelen tener plantillas seguidas).
/// </summary>
public sealed class SegmentName : Decorator
{
    public static readonly DependencyProperty FullTextProperty = DependencyProperty.Register(
        nameof(FullText), typeof(string), typeof(SegmentName), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FromStartProperty = DependencyProperty.Register(
        nameof(FromStart), typeof(bool), typeof(SegmentName), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public string FullText
    {
        get => (string)GetValue(FullTextProperty);
        set => SetValue(FullTextProperty, value);
    }

    public bool FromStart
    {
        get => (bool)GetValue(FromStartProperty);
        set => SetValue(FromStartProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is not TextBlock block)
        {
            return base.MeasureOverride(constraint);
        }

        var full = FullText ?? string.Empty;
        var text = full;
        if (FromStart && double.IsFinite(constraint.Width))
        {
            // Saca caracteres de adelante hasta que "…" y lo que queda entren.
            for (var cut = 1; cut < full.Length && WidthOf(block, text) > constraint.Width; cut++)
            {
                text = "…" + full[cut..];
            }
        }

        if (!string.Equals(block.Text, text, StringComparison.Ordinal))
        {
            block.Text = text;
        }

        block.Measure(constraint);
        return block.DesiredSize;
    }

    private static double WidthOf(TextBlock block, string text) => new FormattedText(
        text,
        CultureInfo.CurrentCulture,
        block.FlowDirection,
        new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch),
        block.FontSize,
        block.Foreground,
        VisualTreeHelper.GetDpi(block).PixelsPerDip).WidthIncludingTrailingWhitespace;
}
