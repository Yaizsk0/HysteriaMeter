using System.IO;
using System.Text.Json;
using HysteriaMeter.App.Dungeon;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Tests;

public sealed partial class UiSnapshotTests
{
    private static readonly State[] FidelityStates =
    [
        new("31-dungeon-ancho-minimo", _ =>
        {
            var wipe = DemoEncounter.Create(Base.AddMinutes(6)) with { Id = Guid.NewGuid(), TargetIsBoss = true };
            var kill = DemoEncounter.Create(Base.AddMinutes(10)) with { Id = Guid.NewGuid(), TargetIsBoss = true, TargetKilled = true };
            return new DungeonWindow(new DungeonSummaryViewModel(DungeonSummary.From([wipe, kill], Base))) { Width = 940 };
        }),
        new("50-overlay-novedades", h =>
        {
            var file = Path.Combine(Path.GetTempPath(), $"hysteria-news-snapshot-{Guid.NewGuid():N}.json");
            var previous = JsonSerializer.Serialize(new { news = NewsFeed.Current.Select(n => new { id = n.Id, text = n.Text }) });
            try
            {
                File.WriteAllText(file, """{"news":[{"id":37,"text":"Una novedad de prueba para revisar la tarjeta completa y el ajuste del texto."}]}""");
                NewsFeed.Load(file);
                return Overlay(Live(h.Demo()));
            }
            finally
            {
                File.WriteAllText(file, previous);
                NewsFeed.Load(file);
                File.Delete(file);
            }
        }),
    ];
}
