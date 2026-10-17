using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.App.Tests;

/// <summary>El registro separado conserva la pelea desde la que se abrió el detalle.</summary>
public sealed class DetalleResumeTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Opening_the_log_of_a_saved_fight_never_shows_another_fights_events()
    {
        var detail = Detail(Fight(Start));
        detail.LogSource = () => [Hit(Start.AddMinutes(10))];
        LogViewModel? opened = null;
        detail.LogRequested += log => opened = log;

        detail.OpenLogCommand.Execute(null);

        Assert.NotNull(opened);
        Assert.Empty(opened.Lines);
        Assert.True(opened.IsEmpty);
    }

    [Fact]
    public void Detached_log_times_start_at_the_fight_start_even_when_the_log_is_truncated()
    {
        var detail = Detail(Fight(Start));
        detail.LogSource = () => [Hit(Start.AddSeconds(65)), Hit(Start.AddSeconds(71))];
        LogViewModel? opened = null;
        detail.LogRequested += log => opened = log;

        detail.OpenLogCommand.Execute(null);

        Assert.NotNull(opened);
        Assert.Equal(["01:05.0", "01:11.0"], opened.Lines.Select(l => l.Time));
        Assert.Equal(1UL, opened.FocusId);
        Assert.Equal("Yo", opened.FocusName);
    }

    [Fact]
    public void Reloading_a_detached_log_keeps_its_original_fight_when_the_detail_changes()
    {
        var detail = Detail(Fight(Start));
        IReadOnlyList<CombatLogEntry> entries = [Hit(Start.AddSeconds(65))];
        detail.LogSource = () => entries;
        LogViewModel? opened = null;
        detail.LogRequested += log => opened = log;
        detail.OpenLogCommand.Execute(null);
        Assert.NotNull(opened);

        detail.Update(Fight(Start.AddMinutes(10)));
        opened.Reload();
        Assert.Equal("01:05.0", Assert.Single(opened.Lines).Time);

        entries = [Hit(Start.AddMinutes(10).AddSeconds(2))];
        opened.Reload();
        Assert.Empty(opened.Lines);
    }

    private static ActorDetailViewModel Detail(EncounterSnapshot fight)
    {
        var row = new ActorRowViewModel();
        row.Update(1, fight.Actors[0], MeterTab.Damage, 4000, 4000, fight.Duration);
        var detail = new ActorDetailViewModel(SkillNames.Empty);
        detail.Select(row);
        detail.Update(fight);
        return detail;
    }

    private static EncounterSnapshot Fight(DateTimeOffset start) =>
        new(Guid.NewGuid(), start, start.AddSeconds(100), "Boss", true, 4000,
            [new ActorSnapshot(1, "Yo", null, true, 4000, 40, 0, 2, 0, 2000, 0, [])]);

    private static CombatLogEntry Hit(DateTimeOffset timestamp) =>
        new(timestamp, LogKind.Damage, 1, "Yo", 900, "Boss", 100, 2000, HitModifiers.None);
}
