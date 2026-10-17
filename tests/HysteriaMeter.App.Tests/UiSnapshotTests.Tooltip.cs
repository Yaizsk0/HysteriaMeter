using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

public sealed partial class UiSnapshotTests
{
    /// <summary>
    /// Tooltip de la primera fila del overlay (mock Estados, "Tooltip de fila"): el contenido del ToolTip del template,
    /// con los datos de su fila, en una ventana propia con el borde de ToolTipDark (fuera de pantalla no se abre el
    /// popup, y un ToolTip no puede ser contenido de una ventana).
    /// </summary>
    private static readonly State[] TooltipStates =
    [
        new("54-tooltip-fila", h => RowTooltip(Live(h.Demo()))),
        new("54b-tooltip-fila-cura", h => RowTooltip(Live(h.Demo(MeterTab.Healing)))),
    ];

    private static Window RowTooltip(MeterViewModel viewModel)
    {
        // El overlay se muestra fuera de pantalla para que arme las filas de su template, como en Renderer.Shot.
        var overlay = Overlay(viewModel);
        overlay.WindowStartupLocation = WindowStartupLocation.Manual;
        overlay.Left = -32000;
        overlay.Top = -32000;
        overlay.ShowActivated = false;
        overlay.ShowInTaskbar = false;
        try
        {
            overlay.Show();
            Pump();
            var row = Descendants<Border>(overlay).FirstOrDefault(b => b.Name == "Row")
                ?? throw new InvalidOperationException("El overlay no armó ninguna fila");
            var tip = row.ToolTip as ToolTip ?? throw new InvalidOperationException("La fila no tiene ToolTip");
            var content = (UIElement)tip.Content;
            tip.Content = null;

            // Lo mismo que dibuja la plantilla de ToolTipDark (Themes/Theme.xaml), con los valores del ToolTip.
            var card = new Border
            {
                BorderThickness = tip.BorderThickness,
                Padding = tip.Padding,
                MaxWidth = tip.MaxWidth,
                CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusCard"),
                Child = content,
            };
            card.SetResourceReference(Border.BackgroundProperty, "PanelSolid");
            card.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            card.SetResourceReference(TextElement.ForegroundProperty, "Text");
            card.SetResourceReference(TextElement.FontFamilyProperty, "BodyFont");
            TextElement.SetFontSize(card, tip.FontSize);

            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                SizeToContent = SizeToContent.WidthAndHeight,
                DataContext = row.DataContext,
                Content = card,
            };
            window.Closed += (_, _) => overlay.Close();
            return window;
        }
        catch
        {
            overlay.Close();
            throw;
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
