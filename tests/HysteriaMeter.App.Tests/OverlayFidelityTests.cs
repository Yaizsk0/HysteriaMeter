using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using HysteriaMeter.App.Overlay;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.App.Tests;

[Collection("Overlay WPF")]
public sealed class OverlayFidelityTests
{
    [Theory]
    [InlineData(260, false)]
    [InlineData(260, true)]
    [InlineData(380, false)]
    [InlineData(380, true)]
    public void Header_keeps_tabs_and_recording_controls_inside_the_frame_without_overlap(double width, bool recording)
    {
        RunOnSta(() =>
        {
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = TestApp.CurrentOrWithoutStartup();
            app.InitializeComponent();
            var recorder = new CaptureRecorder();
            var session = new MeterSession(new CombatEngine(), recorder, SkillNames.Empty,
                new MeterPreferences { LastSeenNews = 1 }, _ => { });
            var vm = new MeterViewModel(session);
            session.Engine.Apply(new LocalPlayerEvent(DateTimeOffset.UnixEpoch, 1, "Local"));
            session.Engine.Apply(new DamageEvent(DateTimeOffset.UnixEpoch, 1, 900, 100, 1000, HitModifiers.None));
            vm.BossOnly = false; // Refresca un encuentro real: la demo fijada no muestra el estado de pausa.
            if (recording)
            {
                recorder.Start(DateTime.UnixEpoch); // Solo publica el estado: no abre archivos ni captura tráfico.
            }

            var window = new OverlayWindow(vm);
            try
            {
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                content.Measure(new Size(width, 420));
                content.Arrange(new Rect(0, 0, width, 420));
                content.UpdateLayout();
                var header = Assert.IsType<Grid>(window.FindName("LiveHeaderGrid"));
                var tabs = Assert.IsType<Border>(window.FindName("LiveTabs"));
                var actions = Assert.IsType<StackPanel>(window.FindName("LiveActions"));
                var tabBounds = tabs.TransformToAncestor(header).TransformBounds(new Rect(tabs.RenderSize));
                var actionBounds = actions.TransformToAncestor(header).TransformBounds(new Rect(actions.RenderSize));
                Assert.True(tabBounds.Width > 0);
                Assert.InRange(tabBounds.Left, 0, header.ActualWidth);
                Assert.InRange(tabBounds.Right, 0, header.ActualWidth + 0.5);
                Assert.InRange(actionBounds.Right, header.ActualWidth - 0.5, header.ActualWidth + 0.5);
                Assert.False(tabBounds.IntersectsWith(actionBounds));
                Assert.NotNull(Assert.IsType<Button>(window.FindName("MoreButton")).ContextMenu);

                // El estado pausado conserva una acción para seguir y no duplica el encabezado/objetivo/pie del vivo.
                vm.IsPaused = true;
                content.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, Assert.IsType<Border>(window.FindName("LiveHeader")).Visibility);
                Assert.Equal(Visibility.Collapsed, Assert.IsType<Border>(window.FindName("TargetBlock")).Visibility);
                Assert.Equal(Visibility.Collapsed, Assert.IsType<Border>(window.FindName("LiveFooter")).Visibility);
                Assert.Equal(Visibility.Visible, Assert.IsType<Border>(window.FindName("PauseHeader")).Visibility);
                Assert.True(Assert.IsType<Button>(window.FindName("ResumeButton")).Command!.CanExecute(null));
            }
            finally
            {
                recorder.Stop();
                window.Close();
            }
        });
    }

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
