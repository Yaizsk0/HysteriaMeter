using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

public class DungeonSummaryViewModelTests
{
    private static readonly DateTimeOffset Entered = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private static ActorSnapshot Player(string name, long damage, int deaths = 0) =>
        new(1, name, 11, name == "Yo", damage, damage / 100.0, 0, 10, 1, 10, 0, [], MainTargetDamage: damage, MainTargetDps: damage / 100.0, Deaths: deaths);

    private static EncounterSnapshot Fight(int start, string target, bool killed, params ActorSnapshot[] actors) =>
        new(Guid.NewGuid(), Entered.AddSeconds(start), Entered.AddSeconds(start + 100), target, killed, actors.Sum(a => a.Damage), actors, TargetIsBoss: true, TargetNpcId: 5);

    [Fact]
    public void Shows_each_boss_with_deaths_and_a_shareable_text()
    {
        var summary = DungeonSummary.From([
            Fight(0, "Kromede", false, Player("Yo", 30_000, deaths: 1), Player("Rubia", 40_000)),
            Fight(200, "Kromede", true, Player("Yo", 50_000), Player("Rubia", 60_000, deaths: 2)),
        ], Entered);

        var view = new DungeonSummaryViewModel(summary);

        var card = Assert.Single(view.Cards);
        Assert.Equal("KILL 1:40", card.Result);
        Assert.Equal([("Rubia", "☠☠"), ("Yo", "☠")], card.Players.Select(p => (p.Name, p.Skulls)));
        Assert.Equal(DungeonSummaryViewModel.BarWidth, card.Players[0].BarWidth);
        Assert.Equal(["KROMEDE"], view.Columns);
        Assert.Equal(("1", "1", "3"), (view.Bosses, view.Wipes, view.Deaths));
        Assert.Equal(2, view.Timeline.Count);
        Assert.Contains("Kromede", view.ShareText, StringComparison.Ordinal);
        Assert.Contains("☠2", view.ShareText, StringComparison.Ordinal);
        Assert.False(view.NoBosses);
    }
}
