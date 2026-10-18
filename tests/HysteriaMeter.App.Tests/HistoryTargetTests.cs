using System.Globalization;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// Historial con datos reales: casi ningún boss tiene nombre en data/npcs.json. Las filas usan el rótulo del overlay
/// ("NPC 2400123") y comparan intentos por plantilla; el trash dice "Trash" y no lleva percentil, récord ni delta.
/// </summary>
public sealed class HistoryTargetTests
{
    private static readonly DateTimeOffset Base = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private static EncounterSummary Saved(
        string id, string? name, uint? npc, bool killed, int seconds = 95, long mainDamage = 900, int attempt = 0, bool record = false, double dps = 1000, int minute = 0)
    {
        var start = Base.AddMinutes(minute);
        return new EncounterSummary(
            id, start.ToString("O", CultureInfo.InvariantCulture), start.AddSeconds(seconds).ToString("O", CultureInfo.InvariantCulture), name, killed, 1000,
            LocalDps: dps, Attempt: attempt, IsRecord: record, LocalName: "Vos", ActorsDamage: 1000, MainTargetDamage: mainDamage, TargetNpcId: npc);
    }

    [Fact]
    public void An_unnamed_boss_is_labelled_like_the_overlay_and_keeps_its_attempt_and_record()
    {
        var item = HistoryItem.From(Saved("w", null, 2400123, killed: false, attempt: 2, record: true));

        Assert.Equal("NPC 2400123", item.Target);
        Assert.Equal("NPC 2400123", item.TargetText);
        Assert.Equal(RunSegmentKind.Wipe, item.Kind);
        Assert.Equal("intento 2", item.AttemptLabel);
        Assert.True(item.IsRecord);
        Assert.Contains("agregá el ID 2400123", item.RowTip, StringComparison.Ordinal);
        Assert.Equal(MeterViewModel.TitleOf(new EncounterSnapshot(Guid.NewGuid(), Base, Base, null, false, 0, [], TargetNpcId: 2400123)), item.Target);
    }

    [Fact]
    public void A_trash_pull_says_trash_and_has_no_attempt_record_or_comparison()
    {
        var trash = HistoryItem.From(Saved("t", null, 7, killed: true, seconds: 12, attempt: 5, record: true));

        Assert.Equal(RunSegmentKind.Trash, trash.Kind);
        Assert.Equal("Trash", trash.TargetText);
        Assert.Empty(trash.AttemptLabel);
        Assert.False(trash.IsRecord);
        Assert.Contains("Objetivo principal: NPC 7", trash.RowTip, StringComparison.Ordinal);
        Assert.True(trash.Matches("trash", HistoryFilter.All, HistoryItem.AllCharacters, Base));

        var compared = HistoryItem.WithDeltas(HistoryItem.WithComparisons([trash, trash with { Id = "t2", LocalDps = 500 }]));
        Assert.All(compared, i => Assert.False(i.HasPercentile));
        Assert.All(compared, i => Assert.Empty(i.VsBest));
        Assert.All(compared, i => Assert.Empty(i.Delta));
    }

    [Fact]
    public void An_old_fight_without_name_or_template_stays_an_encounter_with_nothing_to_compare()
    {
        var wipe = HistoryItem.From(Saved("o", null, null, killed: false));
        var kill = HistoryItem.From(Saved("k", null, null, killed: true));

        // Sin plantilla no se sabe si fue un wipe: queda trash, pero no se lo llama "Trash" (tampoco se puede probar).
        Assert.Equal((RunSegmentKind.Trash, "Encuentro"), (wipe.Kind, wipe.TargetText));
        Assert.Equal((RunSegmentKind.Kill, "Encuentro"), (kill.Kind, kill.TargetText));
        Assert.Empty(kill.AttemptLabel);
        Assert.Equal(BossProgress.NoNameText, BossProgress.For([kill], kill, Base).EmptyText);
    }

    [Fact]
    public void Percentiles_and_deltas_group_unnamed_bosses_by_template_without_counting_trash()
    {
        HistoryItem[] newestFirst =
        [
            HistoryItem.From(Saved("k2", null, 111, killed: true, dps: 1200, minute: 30)),
            HistoryItem.From(Saved("t", null, 111, killed: true, seconds: 10, dps: 5000, minute: 20)),
            HistoryItem.From(Saved("w", null, 111, killed: false, dps: 800, minute: 10)),
            HistoryItem.From(Saved("x", null, 222, killed: true, dps: 3000, minute: 5)),
            HistoryItem.From(Saved("k1", null, 111, killed: true, dps: 1000, minute: 0)),
        ];

        var items = HistoryItem.WithDeltas(HistoryItem.WithComparisons(newestFirst));

        // Contra NPC 111 hay tres intentos (el trash de 5000 no cuenta) y NPC 222 es otro objetivo.
        Assert.Equal([100, null, 33, null, 67], items.Select(i => i.Percentile));
        Assert.Equal("+50%", items[0].Delta); // 1200 contra el wipe de 800
        Assert.Empty(items[1].Delta);
    }

    [Fact]
    public void Boss_progress_follows_the_template_and_has_its_own_text_for_trash()
    {
        HistoryItem[] newestFirst =
        [
            HistoryItem.From(Saved("k", null, 111, killed: true, dps: 1200, minute: 30)),
            HistoryItem.From(Saved("t", null, 111, killed: true, seconds: 10, dps: 5000, minute: 20)),
            HistoryItem.From(Saved("w", null, 111, killed: false, dps: 800, minute: 10)),
        ];

        var progress = BossProgress.For(newestFirst, newestFirst[0], Base);
        Assert.Equal($"{"NPC 111".ToUpper(CultureInfo.CurrentCulture)} · TU PROGRESO", progress.Title);
        Assert.Equal(2, progress.Bars.Count);
        Assert.Equal("1 / 2", progress.Kills);

        Assert.Equal(BossProgress.TrashText, BossProgress.For(newestFirst, newestFirst[1], Base).EmptyText);
        // Sin fila elegida, el trash no cuenta como otro objetivo: queda un solo boss y se grafica.
        Assert.True(BossProgress.For(newestFirst, null, Base).HasData);
    }

    [Fact]
    public void A_run_counts_each_old_kill_without_template_as_its_own_boss()
    {
        HistoryItem[] fights =
        [
            HistoryItem.From(Saved("a", null, null, killed: true)),
            HistoryItem.From(Saved("b", null, null, killed: true, minute: 10)),
            HistoryItem.From(Saved("c", null, 333, killed: false, minute: 20)),
        ];

        Assert.Equal("2/3 bosses · 1 wipe", HistoryRun.BossesText(fights));
    }

    [Fact]
    public void Attempt_and_note_go_on_the_target_line()
    {
        var item = HistoryItem.From(Saved("w", "Kromede", 2300371, killed: false, attempt: 3)) with { Note = "probar\r\notra rotación" };

        Assert.Equal("  intento 3", item.AttemptInline);
        Assert.Equal("  probar otra rotación", item.NoteInline);
        Assert.Empty((item with { Note = null, Attempt = 1 }).NoteInline);
        Assert.Empty(HistoryItem.From(Saved("t", "Lobo", 7, killed: true, seconds: 12, attempt: 3)).AttemptInline);
    }
}
