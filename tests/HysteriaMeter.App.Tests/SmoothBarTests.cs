using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HysteriaMeter.App.Themes;

namespace HysteriaMeter.App.Tests;

/// <summary>La barra de daño se desliza hacia el valor nuevo; el primero y los de Windows sin animaciones saltan.</summary>
public sealed class SmoothBarTests
{
    [Fact]
    public void First_value_is_set_without_animation()
    {
        WithAnimations(true, () =>
        {
            var bar = new ScaleTransform();

            SmoothBar.SetScaleX(bar, 0.4);

            // Sin esto, una fila nueva arrancaría en la escala 1 del transform y se achicaría hasta su valor.
            Assert.Equal(0.4, bar.ScaleX);
            Assert.False(bar.HasAnimatedProperties);
        });
    }

    [Fact]
    public void Next_value_slides_and_stays_at_the_target()
    {
        WithAnimations(true, () =>
        {
            var bar = new ScaleTransform();
            SmoothBar.SetScaleX(bar, 0.4);

            SmoothBar.SetScaleX(bar, 0.9);

            Assert.True(bar.HasAnimatedProperties);
            // El valor base queda en el destino: al terminar (FillBehavior.Stop) la barra se queda ahí.
            Assert.Equal(0.9, (double)bar.GetAnimationBaseValue(ScaleTransform.ScaleXProperty));
        });
    }

    [Fact]
    public void Slide_runs_at_the_capped_frame_rate_and_lets_go_at_the_end()
    {
        OnSta(() =>
        {
            var animation = SmoothBar.Slide(0.2, 0.7);

            // Las ventanas son transparentes (layered): a 60 cuadros por segundo cada barra costaría el doble.
            Assert.Equal(SmoothBar.FrameRate, Timeline.GetDesiredFrameRate(animation));
            Assert.Equal(SmoothBar.Duration, animation.Duration);
            Assert.Equal(FillBehavior.Stop, animation.FillBehavior);
            Assert.Equal(0.2, animation.From);
            Assert.Equal(0.7, animation.To);
            Assert.IsType<CubicEase>(animation.EasingFunction);
        });
    }

    [Fact]
    public void Without_windows_animations_the_bar_jumps()
    {
        WithAnimations(false, () =>
        {
            var bar = new ScaleTransform();
            SmoothBar.SetScaleX(bar, 0.4);

            SmoothBar.SetScaleX(bar, 0.9);

            Assert.False(bar.HasAnimatedProperties);
            Assert.Equal(0.9, bar.ScaleX);
        });
    }

    [Fact]
    public void Turning_animations_off_mid_slide_stops_it_at_the_new_value()
    {
        var enabled = true;
        var previous = SmoothBar.Enabled;
        SmoothBar.Enabled = () => enabled;
        try
        {
            OnSta(() =>
            {
                var bar = new ScaleTransform();
                SmoothBar.SetScaleX(bar, 0.4);
                SmoothBar.SetScaleX(bar, 0.9);
                enabled = false;

                SmoothBar.SetScaleX(bar, 0.6);

                Assert.False(bar.HasAnimatedProperties);
                Assert.Equal(0.6, bar.ScaleX);
            });
        }
        finally
        {
            SmoothBar.Enabled = previous;
        }
    }

    private static void WithAnimations(bool enabled, Action action)
    {
        var previous = SmoothBar.Enabled;
        SmoothBar.Enabled = () => enabled;
        try
        {
            OnSta(action);
        }
        finally
        {
            SmoothBar.Enabled = previous;
        }
    }

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
