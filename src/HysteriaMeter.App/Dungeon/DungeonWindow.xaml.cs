using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Dungeon;

/// <summary>Modo dungeon: resumen de una run con cada boss, sus intentos y las muertes de cada jugador.</summary>
public partial class DungeonWindow : Window
{
    private readonly DungeonSummaryViewModel _viewModel;

    public DungeonWindow(DungeonSummaryViewModel viewModel)
    {
        InitializeComponent();
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
