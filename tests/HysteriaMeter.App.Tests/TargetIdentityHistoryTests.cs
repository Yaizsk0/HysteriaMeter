using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;
using Microsoft.Data.Sqlite;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// El historial y el modo dungeon deciden igual cuándo dos peleas son contra el mismo objetivo (la plantilla primero y,
/// sin plantilla, el nombre) y cuentan los bosses de una run igual. Una celda del modo dungeon abre su intento aunque haya
/// quedado fuera de las últimas peleas cargadas, sin perder la nota.
/// </summary>
[Collection(WpfAppGroup.Name)]
public sealed class TargetIdentityHistoryTests
{
    private static readonly DateTimeOffset Base = new(2026, 10, 16, 21, 0, 0, TimeSpan.Zero);

    private static HistoryItem Item(string id, string? name, uint? npc, bool killed, double dps, int minute, int seconds = 95)
    {
        var start = Base.AddMinutes(minute);
        return HistoryItem.From(new EncounterSummary(
            id, start.ToString("O", CultureInfo.InvariantCulture), start.AddSeconds(seconds).ToString("O", CultureInfo.InvariantCulture), name, killed, 1000,
            LocalDps: dps, Attempt: 1, LocalName: "Vos", ActorsDamage: 1000, MainTargetDamage: 900, TargetNpcId: npc,
            ZoneEnteredAt: Base.ToString("O", CultureInfo.InvariantCulture)));
    }

    private static EncounterSnapshot Snapshot(uint? npc, bool killed, int minute, string? name = null, int seconds = 95) =>
        new(Guid.NewGuid(), Base.AddMinutes(minute), Base.AddMinutes(minute).AddSeconds(seconds), name, killed, 1000L * seconds,
            [new ActorSnapshot(1, "Vos", 15, true, 1000L * seconds, 1000, 0, 10, 2, 100, 0, [], MainTargetDamage: 1000L * seconds, MainTargetDps: 1000)],
            TargetNpcId: npc, ZoneEnteredAt: Base);

    [Fact]
    public void Old_unnamed_attempts_and_new_named_ones_of_the_same_template_compare_together()
    {
        // Wipe y kill de antes de agregar el ID a data/npcs.json ("NPC 111"); el kill de hoy ya dice "Griffith".
        HistoryItem[] newestFirst =
        [
            Item("named", "Griffith", 111, killed: true, dps: 1200, minute: 30),
            Item("kill", null, 111, killed: true, dps: 1000, minute: 10),
            Item("wipe", null, 111, killed: false, dps: 800, minute: 0),
        ];

        var items = HistoryItem.WithDeltas(HistoryItem.WithComparisons(newestFirst));

        Assert.Equal(["Griffith", "NPC 111", "NPC 111"], items.Select(i => i.Target));
        Assert.Equal([100, 67, 33], items.Select(i => i.Percentile));
        Assert.Equal("+20%", items[0].Delta); // 1200 contra el kill de 1000, aunque esa fila diga "NPC 111"

        // TU PROGRESO desde la fila vieja: los tres intentos, con el nombre que tiene hoy.
        var progress = BossProgress.For(items, items[2], Base);
        Assert.Equal($"{"Griffith".ToUpper(CultureInfo.CurrentCulture)} · {BossProgress.Header}", progress.Title);
        Assert.Equal(3, progress.Bars.Count);
        Assert.True(BossProgress.For(items, null, Base).HasData); // sin fila elegida, sigue siendo un solo boss

        // En una run, el wipe sin nombre y el kill con nombre son un boss, no dos.
        Assert.Equal("1/1 boss · 1 wipe", HistoryRun.BossesText([items[0], items[2]]));
        Assert.Contains("Los intentos se siguen contando juntos", items[1].RowTip, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("kill-kill")]
    [InlineData("kill-wipe")]
    [InlineData("wipe-kill-wipe")]
    [InlineData("two-bosses")]
    public void The_history_run_line_counts_bosses_like_the_dungeon_summary(string scenario)
    {
        (uint? Npc, bool Killed)[] fights = scenario switch
        {
            // La misma plantilla muerta dos veces, o un wipe después del kill: dos bosses en las dos vistas.
            "kill-kill" => [(2400400, true), (2400400, true)],
            "kill-wipe" => [(2400400, true), (2400400, false)],
            "wipe-kill-wipe" => [(2400400, false), (2400400, true), (2400400, false)],
            _ => [(2400101, false), (2400101, true), (2400102, true)],
        };

        var dungeon = DungeonSummary.From(fights.Select((f, i) => Snapshot(f.Npc, f.Killed, i * 10)), Base);
        var history = fights.Select((f, i) => Item($"f{i}", null, f.Npc, f.Killed, 1000, i * 10)).Reverse().ToList();

        var wipes = dungeon.Wipes switch { 0 => "sin wipes", 1 => "1 wipe", var n => $"{n} wipes" };
        var bosses = dungeon.Bosses.Count == 1 ? "boss" : "bosses";
        Assert.Equal($"{dungeon.BossesKilled}/{dungeon.Bosses.Count} {bosses} · {wipes}", HistoryRun.BossesText(history));
    }

    [Fact]
    public void A_matrix_cell_opens_its_fight_with_its_note_even_after_it_left_the_loaded_history()
    {
        WithHistory((vm, repository) =>
        {
            var boss = Snapshot(2400123, killed: true, minute: 0);
            Pump(repository.SaveAsync(boss));
            Pump(repository.SetNoteAsync(boss.Id.ToString(), "probar otra rotación"));

            Pump(vm.ToggleHistoryCommand.ExecuteAsync(null));
            DungeonSummaryViewModel? summary = null;
            vm.RunSummaryRequested += s => summary = s;
            Pump(vm.OpenRunSummaryCommand.ExecuteAsync(vm.HistoryRuns.SelectMany(r => r.Fights).Single(f => f.Id == boss.Id.ToString())));
            Assert.NotNull(summary);

            // Llegan más peleas que las que carga el historial y se recarga: la del boss queda afuera.
            Pump(vm.ToggleHistoryCommand.ExecuteAsync(null));
            for (var i = 1; i <= MeterViewModel.HistoryLimit; i++)
            {
                Pump(repository.SaveAsync(Snapshot(7, killed: true, minute: i, seconds: 10) with { ZoneEnteredAt = null }));
            }

            Pump(vm.ToggleHistoryCommand.ExecuteAsync(null));
            Assert.DoesNotContain(vm.HistoryRuns.SelectMany(r => r.Fights), f => f.Id == boss.Id.ToString());

            var cell = summary.Rows.Single().StyledCells.Single();
            Assert.True(summary.OpenFight(cell));
            for (var wait = 0; wait < 200 && vm.PinnedHistoryId != boss.Id.ToString(); wait++)
            {
                Pump(Task.Delay(10));
            }

            Assert.Equal(boss.Id.ToString(), vm.PinnedHistoryId);
            Assert.Equal("probar otra rotación", vm.CurrentNote);
        });
    }

    private static void WithHistory(Action<MeterViewModel, EncounterRepository> action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            var database = Path.Combine(Path.GetTempPath(), $"hysteria-identity-{Guid.NewGuid():N}.db");
            MeterViewModel? vm = null;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                var repository = new EncounterRepository(database);
                Pump(repository.InitializeAsync());
                vm = new MeterViewModel(new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty,
                    new MeterPreferences { LastSeenNews = 1 }, _ => { }, history: repository));
                action(vm, repository);
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                vm?.Stop();
                SqliteConnection.ClearAllPools();
                foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
                {
                    File.Delete(database + suffix);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        _ = task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }
}
