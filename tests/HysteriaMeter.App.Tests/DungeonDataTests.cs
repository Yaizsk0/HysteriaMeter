using System.IO;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// Modo dungeon con datos reales: etiquetas del timeline como el mock, nota por boss solo con lo medido, celdas que abren
/// su intento y "Exportar resumen" con la preferencia de nombres anónimos.
/// </summary>
public sealed class DungeonDataTests
{
    private static readonly DateTimeOffset Entered = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private static ActorSnapshot Player(string name, long damage, int deaths = 0) =>
        new(1, name, 11, name == "Yo", damage, damage / 100.0, 0, 10, 1, 10, 0, [], MainTargetDamage: damage, MainTargetDps: damage / 100.0, Deaths: deaths);

    private static EncounterSnapshot Fight(
        int start, int seconds, uint? npc, bool killed, IReadOnlyList<HealthPhase>? phases = null, params ActorSnapshot[] actors) =>
        new(Guid.NewGuid(), Entered.AddSeconds(start), Entered.AddSeconds(start + seconds), null, killed, actors.Sum(a => a.Damage), actors,
            TargetNpcId: npc, Phases: phases);

    /// <summary>Trash, un wipe a los 1:52 con la vida bajo el 50 % y el kill de 3:58 contra el mismo NPC sin nombre.</summary>
    private static (DungeonSummary Run, EncounterSnapshot Wipe, EncounterSnapshot Kill) Run()
    {
        var trash = Fight(0, 12, 900, killed: true, actors: [Player("Yo", 5_000), Player("Rubia", 4_000)]);
        var wipe = Fight(60, 112, 2400123, killed: false, phases: [new HealthPhase(75, 40), new HealthPhase(50, 90)],
            actors: [Player("Yo", 30_000, deaths: 1), Player("Rubia", 40_000, deaths: 1)]);
        var kill = Fight(300, 238, 2400123, killed: true, actors: [Player("Yo", 50_000), Player("Rubia", 60_000)]);
        return (DungeonSummary.From([trash, wipe, kill], Entered), wipe, kill);
    }

    [Fact]
    public void Timeline_labels_say_the_kill_time_or_a_cross_like_the_mock()
    {
        var view = new DungeonSummaryViewModel(Run().Run);

        Assert.Equal(["Trash", "NPC 2400123 ✕", "NPC 2400123 3:58"], view.Timeline.Select(s => s.Label));
        Assert.Equal([RunSegmentKind.Trash, RunSegmentKind.Wipe, RunSegmentKind.Kill], view.Timeline.Select(s => s.Kind));

        // La ventana recorta solo el nombre: la ✕ y la duración van aparte y siempre se ven. En el tramo, una plantilla sin
        // nombre va sin el "NPC " y, si no entra, se corta por el principio (KeepEnd): lo que la distingue es el final.
        Assert.Equal([("Trash", "", false), ("2400123", "✕", true), ("2400123", "3:58", true)], view.Timeline.Select(s => (s.Name, s.Suffix, s.KeepEnd)));
        Assert.All(view.Timeline.Skip(1), s => Assert.StartsWith("NPC 2400123 · ", s.Tip, StringComparison.Ordinal));
    }

    [Fact]
    public void An_old_fight_without_name_or_template_is_neither_trash_nor_a_wipe()
    {
        var view = new DungeonSummaryViewModel(DungeonSummary.From(
            [Fight(0, 120, null, killed: false, actors: [Player("Yo", 9_000)]), Fight(200, 100, null, killed: true, actors: [Player("Yo", 9_000)])], Entered));

        Assert.Equal(["Encuentro", "Boss 1 1:40"], view.Timeline.Select(s => s.Label));
        Assert.Contains("no se sabe si fue trash o un wipe", view.Timeline[0].Tip, StringComparison.Ordinal);
        Assert.Equal("0", view.Wipes);
    }

    [Fact]
    public void Each_boss_note_only_says_what_was_measured()
    {
        var (run, _, _) = Run();
        Assert.Equal("Wipe 1 a los 1:52 (vida bajo el 50%).", new DungeonSummaryViewModel(run).Cards.Single().Note);

        // Kill al primer intento sin muertes: limpio, con el mejor DPS de ese intento.
        var clean = DungeonSummary.From([Fight(0, 100, 7, killed: true, actors: [Player("Yo", 50_000), Player("Rubia", 30_000)])], Entered);
        Assert.Equal("Kill limpio. Mejor DPS: Yo.", new DungeonSummaryViewModel(clean).Cards.Single().Note);

        // Wipe sin fases medidas: solo el tiempo. Más de tres wipes se resumen.
        var wipes = DungeonSummary.From(
            [.. Enumerable.Range(0, 5).Select(i => Fight(i * 100, 45, 8, killed: false, actors: [Player("Yo", 9_000)]))], Entered);
        Assert.Equal(
            "Wipe 1 a los 0:45. Wipe 2 a los 0:45. Wipe 3 a los 0:45. Y 2 wipes más.",
            new DungeonSummaryViewModel(wipes).Cards.Single().Note);
    }

    [Fact]
    public void A_boss_without_name_or_template_never_claims_a_first_attempt_kill()
    {
        // Peleas viejas: el tramo sin kill queda "Encuentro" (pudo ser un wipe que no se reconoce), así que el "Boss 1"
        // de después no puede decir que cayó al primer intento. Con muertes, solo el mejor DPS.
        var deaths = DungeonSummary.From(
            [Fight(0, 120, null, killed: false, actors: [Player("Yo", 9_000)]),
             Fight(200, 100, null, killed: true, actors: [Player("Yo", 50_000, deaths: 1), Player("Rubia", 30_000)])], Entered);
        var card = new DungeonSummaryViewModel(deaths).Cards.Single();
        Assert.Equal("Boss 1", card.Name);
        Assert.Equal("Mejor DPS: Yo.", card.Note);
        Assert.False(deaths.Bosses.Single().Identified);

        // Sin muertes sí se midió que fue limpio.
        var clean = DungeonSummary.From([Fight(0, 100, null, killed: true, actors: [Player("Yo", 50_000)])], Entered);
        Assert.Equal("Kill limpio. Mejor DPS: Yo.", new DungeonSummaryViewModel(clean).Cards.Single().Note);

        // Con plantilla, un kill sin wipes con muertes sí fue al primer intento.
        var identified = DungeonSummary.From([Fight(0, 100, 2400123, killed: true, actors: [Player("Yo", 50_000, deaths: 1)])], Entered);
        Assert.True(identified.Bosses.Single().Identified);
        Assert.Equal("Kill al primer intento. Mejor DPS: Yo.", new DungeonSummaryViewModel(identified).Cards.Single().Note);
    }

    [Fact]
    public void A_matrix_cell_opens_the_attempt_that_gives_its_dps()
    {
        var (run, _, kill) = Run();
        var opened = new List<Guid>();
        var view = new DungeonSummaryViewModel(run, opened.Add);

        var cell = view.Rows[0].StyledCells.Single();
        Assert.Equal(kill.Id, cell.FightId);
        Assert.True(cell.CanOpen);
        Assert.Contains("abre el kill contra NPC 2400123", cell.Tip, StringComparison.Ordinal);
        Assert.True(view.OpenFight(cell));
        Assert.Equal([kill.Id], opened);

        // Sin dónde abrirlo (vista previa), la celda queda quieta.
        var still = new DungeonSummaryViewModel(run).Rows[0].StyledCells.Single();
        Assert.False(still.CanOpen);
        Assert.False(new DungeonSummaryViewModel(run).OpenFight(still));
        Assert.False(view.OpenFight(new PlayerBossCell(string.Empty, 0)));
    }

    [Fact]
    public void Export_writes_the_run_csv_and_hides_the_others_names_when_asked()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hysteria-dungeon-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (run, _, _) = Run();
            var (path, error) = new DungeonSummaryViewModel(run, anonymize: true, exportFolder: folder).Export();

            Assert.Null(error);
            Assert.Equal(Path.Combine(folder, $"{EncounterExport.RunFileName(Entered.ToLocalTime())}.csv"), path);
            var bytes = File.ReadAllBytes(path!);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            var csv = File.ReadAllText(path!);
            Assert.StartsWith("boss;entrada;intentos;wipes", csv, StringComparison.Ordinal);
            Assert.Contains("NPC 2400123;", csv, StringComparison.Ordinal);
            Assert.Contains(";Jugador 1;", csv, StringComparison.Ordinal);
            Assert.DoesNotContain("Rubia", csv, StringComparison.Ordinal);

            var shared = new DungeonSummaryViewModel(run, anonymize: true).ShareText;
            Assert.DoesNotContain("Rubia", shared, StringComparison.Ordinal);
            Assert.Contains("Jugador 1", shared, StringComparison.Ordinal);
            Assert.Contains("Rubia", new DungeonSummaryViewModel(run).ShareText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void Export_reports_why_it_could_not_save()
    {
        // Una carpeta que es un archivo: no se puede crear.
        var file = Path.GetTempFileName();
        try
        {
            var (path, error) = new DungeonSummaryViewModel(Run().Run, exportFolder: file).Export();

            Assert.Null(path);
            Assert.StartsWith("No se pudo guardar", error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
