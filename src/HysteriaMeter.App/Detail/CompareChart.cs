using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Detail;

/// <summary>
/// "DPS acumulado" de la vista Comparar (mock Comparar): tu curva llena y la del otro punteada, sobre el mismo eje de
/// segundos (el más largo de los dos), con un círculo y "☠ 1:52" en cada muerte.
/// </summary>
public sealed class CompareChart : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(CompareChartData), typeof(CompareChart),
        new FrameworkPropertyMetadata(CompareChartData.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MineBrushProperty = DependencyProperty.Register(
        nameof(MineBrush), typeof(Brush), typeof(CompareChart),
        new FrameworkPropertyMetadata(Brushes.Gold, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TheirsBrushProperty = DependencyProperty.Register(
        nameof(TheirsBrush), typeof(Brush), typeof(CompareChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DeathBrushProperty = DependencyProperty.Register(
        nameof(DeathBrush), typeof(Brush), typeof(CompareChart),
        new FrameworkPropertyMetadata(Brushes.IndianRed, FrameworkPropertyMetadataOptions.AffectsRender));

    // Trazo del otro lado: guiones de 6 y huecos de 5 px con grosor 2, como el mock (la DashStyle va en múltiplos del grosor).
    private static readonly DashStyle Dashed = Freeze(new DashStyle([3, 2.5], 0));

    private const double MineThickness = 2.5;
    private const double TheirsThickness = 2;
    private const double DeathRadius = 5;

    // Margen arriba y abajo para que el trazo y los círculos no se corten en el borde.
    private const double Inset = 8;

    public CompareChartData Data
    {
        get => (CompareChartData)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Brush MineBrush
    {
        get => (Brush)GetValue(MineBrushProperty);
        set => SetValue(MineBrushProperty, value);
    }

    public Brush TheirsBrush
    {
        get => (Brush)GetValue(TheirsBrushProperty);
        set => SetValue(TheirsBrushProperty, value);
    }

    public Brush DeathBrush
    {
        get => (Brush)GetValue(DeathBrushProperty);
        set => SetValue(DeathBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var data = Data;
        var seconds = Math.Max(data.Mine.Count, data.Theirs.Count);
        if (seconds < 2 || ActualWidth <= 0 || ActualHeight <= 2 * Inset)
        {
            return;
        }

        var top = Math.Max(data.Mine.DefaultIfEmpty().Max(), data.Theirs.DefaultIfEmpty().Max());
        if (top <= 0)
        {
            return;
        }

        // Fondo transparente: el tooltip aparece en todo el recuadro, no solo sobre las líneas.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        Point At(int second, double dps) => new(
            second * ActualWidth / (seconds - 1),
            Inset + (1 - (dps / top)) * (ActualHeight - 2 * Inset));

        Draw(drawingContext, data.Theirs, new Pen(TheirsBrush, TheirsThickness) { DashStyle = Dashed, LineJoin = PenLineJoin.Round }, At);
        Draw(drawingContext, data.Mine, new Pen(MineBrush, MineThickness) { LineJoin = PenLineJoin.Round }, At);
        Deaths(drawingContext, data.Theirs, data.TheirDeaths, At);
        Deaths(drawingContext, data.Mine, data.MineDeaths, At);
    }

    private static void Draw(DrawingContext context, IReadOnlyList<double> series, Pen pen, Func<int, double, Point> at)
    {
        if (series.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(at(0, series[0]), isFilled: false, isClosed: false);
            for (var i = 1; i < series.Count; i++)
            {
                figure.LineTo(at(i, series[i]), isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    private void Deaths(DrawingContext context, IReadOnlyList<double> series, IReadOnlyList<int> deaths, Func<int, double, Point> at)
    {
        if (series.Count == 0)
        {
            return;
        }

        var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var second in deaths)
        {
            var index = Math.Clamp(second, 0, series.Count - 1);
            var point = at(index, series[index]);
            context.DrawEllipse(DeathBrush, null, point, DeathRadius, DeathRadius);
            var label = new FormattedText(
                "☠ " + TimeSpan.FromSeconds(second).ToString(@"m\:ss", CultureInfo.InvariantCulture),
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, DeathBrush, pixelsPerDip);

            // Abajo a la derecha del círculo, como el mock; si no entra, del otro lado.
            var x = point.X + 12 + label.Width <= ActualWidth ? point.X + 12 : point.X - 12 - label.Width;
            var y = point.Y + 6 + label.Height <= ActualHeight ? point.Y + 6 : point.Y - 6 - label.Height;
            context.DrawText(label, new Point(x, y));
        }
    }

    private static DashStyle Freeze(DashStyle style)
    {
        style.Freeze();
        return style;
    }
}
