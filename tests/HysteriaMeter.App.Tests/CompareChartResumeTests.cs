using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HysteriaMeter.App.Detail;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

/// <summary>El tiempo sin golpes también cuenta en la curva acumulada y en la posición de las muertes.</summary>
public sealed class CompareChartResumeTests
{
    [Fact]
    public void Comparison_keeps_accumulating_elapsed_time_until_each_fight_ends()
    {
        var start = DateTimeOffset.UnixEpoch;
        var mine = Actor(1, Enumerable.Repeat(100L, 31).ToArray());
        var theirs = Actor(2, Enumerable.Repeat(200L, 61).ToArray());
        var fight = new EncounterSnapshot(Guid.NewGuid(), start, start.AddSeconds(120), "Boss", true, 3100, [mine]);
        var saved = new EncounterSnapshot(Guid.NewGuid(), start.AddDays(-1), start.AddDays(-1).AddSeconds(90), "Boss", true, 12200, [theirs]);
        var row = new ActorRowViewModel();
        row.Update(1, mine, MeterTab.Damage, 3100, 3100, fight.Duration);
        var detail = new ActorDetailViewModel(SkillNames.Empty);
        detail.Select(row);
        detail.Update(fight);

        detail.CompareWith = new CompareOption(CompareOption.PreviousAttemptId, "Anterior", theirs, SavedEncounter: saved);

        var data = detail.CompareChart;
        // Índices 0..120 y 0..90: el eje representa segundos reales, incluido el final de cada pelea.
        Assert.Equal(121, data.Mine.Count);
        Assert.Equal(91, data.Theirs.Count);
        Assert.Equal(100, data.Mine[30]);
        Assert.Equal(3100d / 61, data.Mine[60], precision: 6);
        Assert.Equal(3100d / 121, data.Mine[120], precision: 6);
        Assert.Equal(12200d / 91, data.Theirs[90], precision: 6);
    }

    [Theory]
    [InlineData(60, 600)]
    [InlineData(180, 1200)]
    public void Death_after_the_last_hit_is_drawn_at_its_real_second(int deathSecond, double expectedX)
    {
        OnSta(() =>
        {
            var chart = new CompareChart
            {
                Width = 1200,
                Height = 120,
                Data = new CompareChartData(
                    Enumerable.Repeat(100d, 31).ToArray(),
                    Enumerable.Repeat(200d, 121).ToArray(),
                    [deathSecond],
                    []),
            };
            chart.Measure(new Size(1200, 120));
            chart.Arrange(new Rect(0, 0, 1200, 120));
            chart.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1200, 120, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(chart);

            var drawing = VisualTreeHelper.GetDrawing(chart);
            Assert.NotNull(drawing);
            var marker = Assert.Single(Geometries(drawing).OfType<EllipseGeometry>());
            // A 60 s cae en el centro del eje de 120 s; a 180 s amplía el eje y cae al final.
            Assert.Equal(expectedX, marker.Center.X, precision: 6);
        });
    }

    private static ActorSnapshot Actor(ulong id, long[] timeline) =>
        new(id, $"Jugador {id}", null, true, timeline.Sum(), 100, 1, timeline.Length, 0, 100, 0, [], Timeline: timeline);

    private static IEnumerable<Geometry> Geometries(Drawing drawing) => drawing switch
    {
        GeometryDrawing geometry => [geometry.Geometry],
        DrawingGroup group => group.Children.SelectMany(Geometries),
        _ => [],
    };

    private static void OnSta(Action action)
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
