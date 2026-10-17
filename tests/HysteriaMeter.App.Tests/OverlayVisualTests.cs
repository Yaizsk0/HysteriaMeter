using System.Runtime.ExceptionServices;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

/// <summary>Lo que el overlay dibuja distinto por la revisión visual contra los mocks (paso 7).</summary>
public sealed class OverlayVisualTests
{
    [Theory]
    [InlineData(0, "")]
    [InlineData(1, " ✝")]
    [InlineData(3, " ✝3")]
    public void Death_mark_goes_apart_from_the_name_for_the_symbol_font(int deaths, string mark)
    {
        var row = new ActorRowViewModel();
        row.Update(1, Actor(deaths), MeterTab.Damage, top: 3000, total: 3000, TimeSpan.FromSeconds(10));

        Assert.Equal("Tharos", row.Name);
        Assert.Equal(mark, row.DeathText);
        Assert.Equal("Tharos" + mark, row.DisplayName);
    }

    [Fact]
    public void Pause_hint_points_to_the_bars_button_when_there_is_no_hotkey()
    {
        RunOnSta(() =>
        {
            var withHotkey = Meter(new MeterPreferences { HotkeyPause = "Ctrl+Shift+P" });
            Assert.Equal("Ctrl+Shift+P para seguir", withHotkey.PauseHint);

            // El botón de la franja dibuja dos barras, no "▶".
            var without = Meter(new MeterPreferences { HotkeyPause = string.Empty });
            Assert.Equal(MeterViewModel.PauseHintWithoutHotkey, without.PauseHint);
            Assert.DoesNotContain("▶", without.PauseHint, StringComparison.Ordinal);
        });
    }

    private static ActorSnapshot Actor(int deaths) =>
        new(7, "Tharos", null, false, 3000, 300, 1, 10, 2, 900, 0, [], Deaths: deaths);

    private static MeterViewModel Meter(MeterPreferences preferences) =>
        new(new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, preferences with { LastSeenNews = 1 }, _ => { }));

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
