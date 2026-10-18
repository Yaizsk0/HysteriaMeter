using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// El texto para compartir una pelea nombra el objetivo igual que el título del overlay, el historial y el modo dungeon:
/// "NPC 2400123" si la plantilla no tiene nombre y "Encuentro" solo sin nombre ni plantilla.
/// </summary>
public sealed class ShareTargetLabelTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 21, 0, 0, TimeSpan.Zero);

    private static EncounterSnapshot Fight(string? name, uint? npc) => new(
        Guid.NewGuid(), Start, Start.AddSeconds(95), name, TargetKilled: true, TotalDamage: 9_500,
        [new ActorSnapshot(1, "Yo", 11, true, 9_500, 100, 0, 10, 1, 900, 0, [])], TargetNpcId: npc);

    [Theory]
    [InlineData(null, 2400123u, "NPC 2400123 01:35 ")]
    [InlineData("Kromede", 2400123u, "Kromede 01:35 ")]
    [InlineData("#81234", null, "Encuentro 01:35 ")]
    [InlineData(null, null, "Encuentro 01:35 ")]
    public void The_shared_summary_starts_with_the_same_label_as_the_overlay_title(string? name, uint? npc, string header)
    {
        var fight = Fight(name, npc);

        Assert.StartsWith(header, MeterViewModel.BuildSummary(fight, MeterTab.Damage), StringComparison.Ordinal);
        Assert.StartsWith(MeterViewModel.TitleOf(fight) + " ", header, StringComparison.Ordinal);
    }
}
