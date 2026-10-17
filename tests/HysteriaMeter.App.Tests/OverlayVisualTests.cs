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

    [Theory]
    [InlineData(22, 13, 4, 2)]
    [InlineData(30, 17, 5, 3)]
    [InlineData(36, 18, 5, 3)]
    public void Each_density_uses_the_mock_radius_gap_and_dps_size(int rowHeight, double dps, double radius, double gap)
    {
        Assert.Equal(dps, MeterViewModel.RowValueSizeFor(rowHeight));
        Assert.Equal(new System.Windows.CornerRadius(radius), MeterViewModel.RowCornerRadiusFor(rowHeight));
        Assert.Equal(new System.Windows.Thickness(0, 0, 0, gap), MeterViewModel.RowGapFor(rowHeight));
    }

    [Fact]
    public void Changing_the_density_notifies_radius_and_gap()
    {
        RunOnSta(() =>
        {
            var meter = Meter(new MeterPreferences { RowHeight = 30 });
            var changed = new List<string?>();
            meter.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            meter.RowHeight = 22;

            Assert.Contains(nameof(MeterViewModel.RowCornerRadius), changed);
            Assert.Contains(nameof(MeterViewModel.RowGap), changed);
            Assert.Equal(new System.Windows.CornerRadius(4), meter.RowCornerRadius);
        });
    }

    [Fact]
    public void Tooltip_hides_crits_when_there_is_no_value()
    {
        var row = new ActorRowViewModel();
        Assert.False(row.HasCrit);

        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // El meter la llena solo en DPS y cuando hubo golpes.
        row.Crit = "12 %";
        Assert.True(row.HasCrit);
        Assert.Contains(nameof(ActorRowViewModel.HasCrit), changed);

        row.Crit = "\u2013";
        Assert.False(row.HasCrit);
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
