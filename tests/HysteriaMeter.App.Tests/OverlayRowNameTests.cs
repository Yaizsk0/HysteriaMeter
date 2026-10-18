using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// Nombre de la fila del overlay, como los tramos del dungeon: si no entra se recorta solo el nombre; la cruz de muertes
/// y la flecha ▲/▼ van pegadas a continuación y se ven siempre, también en el ancho mínimo, en compacto y en pausa.
/// </summary>
[Collection("Overlay WPF")]
public sealed class OverlayRowNameTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Death_mark_and_trend_stay_visible_right_after_a_trimmed_name(bool compact)
    {
        RunOnSta(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = TestApp.CurrentOrWithoutStartup();
            app.InitializeComponent();
            var session = new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty,
                new MeterPreferences { LastSeenNews = 1 }, _ => { });
            var vm = new MeterViewModel(session, compact: compact);
            vm.ShowDemoCommand.Execute(null);
            // Tharos es el de la demo con una muerte. El nombre largo es de prueba y la flecha se fija a mano: en vivo la
            // pone RankTrends y dura 3 s.
            var tharos = vm.Actors.Single(a => a.Name == "Tharos");
            tharos.Name = "Nombre de prueba muy largo";
            tharos.Trend = "▲";
            var brann = vm.Actors.Single(a => a.Name == "Brann");
            brann.Trend = "▼";
            var window = new OverlayWindow(vm);
            try
            {
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                content.Measure(new Size(260, 420));
                content.Arrange(new Rect(0, 0, 260, 420));
                content.UpdateLayout();

                // Nombre largo: se recorta y la cruz y la flecha quedan enteras, a continuación y antes del DPS.
                var (name, death, trend, dps) = Cells(content, tharos);
                Assert.Equal(compact ? 13 : vm.RowNameSize, name.FontSize);
                Assert.Equal(name.FontSize, death.FontSize);
                Assert.True(name.ActualWidth < FullWidth(name) - 1, $"el nombre mide {name.ActualWidth} px: no se recortó");
                Assert.True(death.ActualWidth > 0, "la cruz de muertes quedó sin ancho");
                Assert.True(trend.ActualWidth > 0, "la flecha quedó sin ancho");
                Assert.InRange(Bounds(death, content).Left - Bounds(name, content).Right, -0.5, 0.5);
                Assert.InRange(Bounds(trend, content).Left - Bounds(death, content).Right, 7.5, 8.5);
                Assert.True(Bounds(trend, content).Right <= Bounds(dps, content).Left + 0.5, "la flecha pisa el DPS");

                // Nombre corto, sin muertes: entra entero y la flecha va pegada a él, no al borde derecho de la columna.
                var (shortName, shortDeath, shortTrend, _) = Cells(content, brann);
                Assert.Equal(Visibility.Collapsed, shortDeath.Visibility);
                Assert.InRange(shortName.ActualWidth, shortName.DesiredSize.Width - 0.5, shortName.DesiredSize.Width + 0.5);
                Assert.InRange(Bounds(shortTrend, content).Left - Bounds(shortName, content).Right, 7.5, 8.5);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Death_mark_stays_visible_in_the_paused_row(bool compact)
    {
        RunOnSta(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = TestApp.CurrentOrWithoutStartup();
            app.InitializeComponent();
            var session = new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty,
                new MeterPreferences { LastSeenNews = 1 }, _ => { });
            var vm = new MeterViewModel(session, compact: compact);
            vm.ShowDemoCommand.Execute(null);
            // En pausa la fila muestra solo puesto, nombre, cruz y DPS (la franja de pausa).
            vm.IsViewingHistory = false;
            vm.IsPaused = true;
            Assert.True(vm.IsPauseBannerVisible);
            var tharos = vm.Actors.Single(a => a.Name == "Tharos");
            tharos.Name = "Nombre de prueba muy largo, más que la fila en pausa";
            var window = new OverlayWindow(vm);
            try
            {
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                content.Measure(new Size(260, 420));
                content.Arrange(new Rect(0, 0, 260, 420));
                content.UpdateLayout();

                var name = Cell(content, tharos, "PauseRowName");
                var death = Cell(content, tharos, "PauseRowDeath");
                var dps = Cell(content, tharos, "PauseRowDps");
                Assert.Equal(compact ? 13 : 15, name.FontSize);
                Assert.Equal(name.FontSize, death.FontSize);
                Assert.True(name.ActualWidth < FullWidth(name) - 1, $"el nombre mide {name.ActualWidth} px: no se recortó");
                Assert.True(death.ActualWidth > 0, "la cruz de muertes quedó sin ancho");
                Assert.InRange(Bounds(death, content).Left - Bounds(name, content).Right, -0.5, 0.5);
                Assert.True(Bounds(death, content).Right <= Bounds(dps, content).Left + 0.5, "la cruz pisa el DPS");
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>Nombre, cruz, flecha y DPS de la fila de <paramref name="row"/>: los elementos con nombre de su plantilla.</summary>
    private static (TextBlock Name, TextBlock Death, TextBlock Trend, TextBlock Dps) Cells(DependencyObject root, ActorRowViewModel row) =>
        (Cell(root, row, "NameText"), Cell(root, row, "DeathMark"), Cell(root, row, "TrendText"), Cell(root, row, "PerSecondText"));

    private static TextBlock Cell(DependencyObject root, ActorRowViewModel row, string name) =>
        Descendants(root).OfType<TextBlock>().Single(t => ReferenceEquals(t.DataContext, row) && t.Name == name);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    /// <summary>
    /// Ancho del texto entero, sin recortar (como SegmentName.WidthOf en el dungeon). Se arma con los Run: con Inlines
    /// (el puesto y el nombre de la pausa), Text queda vacío.
    /// </summary>
    private static double FullWidth(TextBlock block) => new FormattedText(
        string.Concat(block.Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text)),
        CultureInfo.CurrentCulture,
        block.FlowDirection,
        new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch),
        block.FontSize,
        block.Foreground,
        VisualTreeHelper.GetDpi(block).PixelsPerDip).WidthIncludingTrailingWhitespace;

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
