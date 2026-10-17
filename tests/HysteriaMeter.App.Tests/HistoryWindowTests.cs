using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using HysteriaMeter.App.History;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;
using Microsoft.Data.Sqlite;

namespace HysteriaMeter.App.Tests;

/// <summary>
/// Tests que crean o reusan la App de WPF fuera de WindowSmokeTests. WPF admite una sola App por proceso: esta colección
/// no corre en paralelo con las demás (xunit la deja para el final), así no compite con WindowSmokeTests por crearla.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfAppGroup
{
    public const string Name = "App de WPF";
}

/// <summary>
/// Ventana del historial: se abre con peleas guardadas de verdad (SQLite temporal) y su lista, filtros y columna de
/// progreso responden como en la app. Más las reglas de las runs, los deltas, el progreso y las fechas.
/// </summary>
[Collection(WpfAppGroup.Name)]
public sealed class HistoryWindowTests
{
    private static readonly DateTimeOffset Base = new(2026, 10, 15, 21, 30, 0, TimeSpan.Zero);

    [Fact]
    public void History_window_opens_with_runs_and_marks_the_open_fight()
    {
        var database = Path.Combine(Path.GetTempPath(), $"hysteria-history-window-{Guid.NewGuid():N}.db");
        try
        {
            RunOnSta(() =>
            {
                Application.ResourceAssembly ??= typeof(App).Assembly;
                if (Application.Current is not App)
                {
                    new App().InitializeComponent();
                }

                var repository = new EncounterRepository(database);
                Pump(repository.InitializeAsync());
                var entered = Base.AddHours(-2);
                EncounterSnapshot[] fights =
                [
                    DemoEncounter.Create(entered.AddMinutes(4)) with { Id = Guid.NewGuid(), ZoneEnteredAt = entered, TargetName = null },
                    DemoEncounter.Create(entered.AddMinutes(9)) with { Id = Guid.NewGuid(), ZoneEnteredAt = entered },
                    DemoEncounter.Create(entered.AddMinutes(14)) with { Id = Guid.NewGuid(), ZoneEnteredAt = entered, TargetKilled = true },
                    DemoEncounter.Create(Base.AddDays(-10)) with { Id = Guid.NewGuid() },
                ];
                foreach (var fight in fights)
                {
                    Pump(repository.SaveAsync(fight));
                }

                var session = new MeterSession(
                    new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences { LastSeenNews = 1 }, _ => { }, history: repository);
                var vm = new MeterViewModel(session) { HistoryClock = () => Base.AddHours(1) };
                Pump(vm.ToggleHistoryCommand.ExecuteAsync(null));

                // Una run con su resumen y la pelea suelta de hace 10 días.
                Assert.True(vm.IsHistoryOpen);
                Assert.Equal("4 peleas · 1 run", vm.HistoryCountText);
                Assert.False(vm.HasHistoryMoreText); // entran todas en la lista
                Assert.Equal(2, vm.HistoryRuns.Count);
                var run = vm.HistoryRuns[0];
                Assert.True(run.CanSummarize);
                Assert.StartsWith("Run · entrada ", run.Title, StringComparison.Ordinal);
                Assert.Equal("1/1 boss · 1 wipe", run.Summary);
                Assert.Equal([RunSegmentKind.Kill, RunSegmentKind.Wipe, RunSegmentKind.Trash], run.Fights.Select(f => f.Kind));
                Assert.Equal(1, run.Fights[0].Deaths);
                Assert.False(vm.HistoryRuns[1].CanSummarize);
                Assert.False(vm.HasProgress); // dos objetivos ("Encuentro" y el muñeco) y ninguna fila elegida
                Assert.Equal(BossProgress.PickText, vm.Progress.EmptyText);

                // Los chips son excluyentes y filtran sobre todas las peleas cargadas.
                vm.FilterBosses = true;
                Assert.False(vm.FilterAll);
                Assert.Equal("3 peleas · 1 run", vm.HistoryCountText);
                Assert.True(vm.HasProgress); // sin el trash queda un solo objetivo
                vm.FilterThisWeek = true;
                Assert.False(vm.FilterBosses);
                Assert.Equal("3 peleas · 1 run", vm.HistoryCountText);
                vm.OnlyKills = true;
                Assert.Equal(HistoryFilter.Kills, vm.HistoryFilter);
                Assert.Single(vm.HistoryRuns.SelectMany(r => r.Fights));
                vm.FilterAll = true;
                vm.HistorySearch = "no coincide";
                Assert.Empty(vm.HistoryRuns);
                Assert.True(vm.HasHistoryEmptyText);
                vm.HistorySearch = string.Empty;
                Assert.False(vm.HasHistoryEmptyText);

                // La configuración del overlay no cierra la ventana del historial.
                vm.ToggleSettingsCommand.Execute(null);
                Assert.True(vm.IsHistoryOpen);

                // Abrir una pelea deja la ventana abierta, marca la fila, grafica ese boss y saca la configuración de
                // encima de las filas del overlay.
                var kill = vm.HistoryRuns[0].Fights[0];
                Pump(vm.OpenEncounterCommand.ExecuteAsync(kill));
                Assert.True(vm.IsHistoryOpen);
                Assert.False(vm.IsSettingsOpen);
                Assert.Equal(kill.Id, vm.PinnedHistoryId);
                Assert.True(vm.HasProgress);
                Assert.Equal("1 / 3", vm.Progress.Kills);
                Assert.Equal(3, vm.Progress.Bars.Count);

                var window = new HistoryWindow(vm);
                Layout(window);

                // Borrar la pelea abierta (desde el overlay) vuelve al vivo y la saca de la lista abierta.
                Pump(vm.DeleteEncounterCommand.ExecuteAsync(null));
                Assert.Null(vm.PinnedHistoryId);
                Assert.Equal("3 peleas · 1 run", vm.HistoryCountText);

                // Sin base: la lista vacía lo explica.
                var empty = new MeterViewModel(new MeterSession(
                    new CombatEngine(), new CaptureRecorder(), SkillNames.Empty, new MeterPreferences { LastSeenNews = 1 }, _ => { }));
                Pump(empty.ToggleHistoryCommand.ExecuteAsync(null));
                Assert.True(empty.HasHistoryEmptyText);
                Layout(new HistoryWindow(empty));

                SqliteConnection.ClearAllPools();
            });
        }
        finally
        {
            foreach (var file in new[] { database, database + "-wal", database + "-shm" })
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void Kills_show_the_change_against_the_previous_attempt()
    {
        HistoryItem[] newestFirst =
        [
            Item("d", "Kromede", RunSegmentKind.Kill, 90),
            Item("c", "Kromede", RunSegmentKind.Kill, 110),
            Item("b", "Kromede", RunSegmentKind.Wipe, 100),
            Item("a", "Kromede", RunSegmentKind.Kill, 80),
            Item("x", "Encuentro", RunSegmentKind.Kill, 50),
        ];

        var items = HistoryItem.WithDeltas(newestFirst);

        Assert.Equal("−18%", items[0].Delta); // 90 contra 110
        Assert.False(items[0].DeltaPositive);
        Assert.Equal("+10%", items[1].Delta); // 110 contra el wipe de 100
        Assert.True(items[1].DeltaPositive);
        Assert.Empty(items[2].Delta); // los wipes no llevan delta
        Assert.Empty(items[3].Delta); // el primer intento no tiene contra qué
        Assert.Empty(items[4].Delta); // sin nombre de objetivo no hay intentos
    }

    [Fact]
    public void Chips_filter_kills_bosses_and_the_last_seven_days()
    {
        var now = Base;
        var kill = Item("k", "Kromede", RunSegmentKind.Kill, 100, started: now.AddDays(-2));
        var wipe = Item("w", "Kromede", RunSegmentKind.Wipe, 100, started: now.AddDays(-8));
        var trash = Item("t", "Encuentro", RunSegmentKind.Trash, 100, started: now.AddHours(-1));

        bool[] Pass(HistoryFilter filter) => [.. new[] { kill, wipe, trash }.Select(i => i.Matches(null, filter, HistoryItem.AllCharacters, now))];

        Assert.Equal([true, true, true], Pass(HistoryFilter.All));
        Assert.Equal([true, false, false], Pass(HistoryFilter.Kills));
        Assert.Equal([true, true, false], Pass(HistoryFilter.Bosses));
        Assert.Equal([true, false, true], Pass(HistoryFilter.ThisWeek));
        Assert.False(kill.Matches("tarka", HistoryFilter.All, HistoryItem.AllCharacters, now));
        Assert.False(kill.Matches(null, HistoryFilter.All, "Otro", now));
    }

    [Fact]
    public void Runs_group_consecutive_fights_by_zone_entry()
    {
        var entered = Base.ToString("O", CultureInfo.InvariantCulture);
        HistoryItem[] all =
        [
            Item("4", "Suelta", RunSegmentKind.Trash, 10, started: Base.AddHours(2)),
            Item("3", "Kromede", RunSegmentKind.Kill, 10, run: entered, started: Base.AddMinutes(20)) with { Ended = Base.AddMinutes(24) },
            Item("2", "Kromede", RunSegmentKind.Wipe, 10, run: entered, started: Base.AddMinutes(10)),
            Item("1", "Tarka", RunSegmentKind.Wipe, 10, run: entered, started: Base.AddMinutes(5)),
        ];

        // El filtro oculta un wipe: el resumen de la run sigue contando todas sus peleas.
        var runs = HistoryRun.Group([all[0], all[1], all[3]], all);

        Assert.Equal(2, runs.Count);
        Assert.False(runs[0].CanSummarize);
        Assert.StartsWith("Peleas sueltas · ", runs[0].Title, StringComparison.Ordinal);
        Assert.True(runs[1].CanSummarize);
        Assert.Equal($"Run · entrada {Base.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)}", runs[1].Title);
        Assert.Equal("3 peleas · 24:00", runs[1].When);
        Assert.Equal("1/2 bosses · 2 wipes", runs[1].Summary);
        Assert.Equal(["3", "1"], runs[1].Fights.Select(f => f.Id));
        Assert.Same(all[1], runs[1].First);
        Assert.Equal("4 peleas · 1 run", HistoryRun.CountText(all));
        Assert.Equal("1 pelea · 0 runs", HistoryRun.CountText([all[0]]));
        Assert.Equal("sin bosses", HistoryRun.BossesText([all[0]]));
        Assert.Equal("1/1 boss · sin wipes", HistoryRun.BossesText([all[1]]));
    }

    [Fact]
    public void Progress_follows_the_selected_fight_or_the_only_boss_left()
    {
        var now = Base;
        HistoryItem[] newestFirst =
        [
            Item("c", "Kromede", RunSegmentKind.Kill, 300, started: now.AddDays(-1)) with { Deaths = 2 },
            Item("b", "Kromede", RunSegmentKind.Wipe, 100, started: now.AddDays(-3)) with { Deaths = 5 },
            Item("a", "Kromede", RunSegmentKind.Wipe, 200, started: now.AddDays(-21)),
            Item("x", "Tarka", RunSegmentKind.Kill, 900, started: now),
        ];

        // Sin fila elegida y con dos bosses: no hay a quién graficar.
        var none = BossProgress.For(newestFirst, null, now);
        Assert.False(none.HasData);
        Assert.Equal(BossProgress.PickText, none.EmptyText);
        Assert.Equal(BossProgress.Header, none.Title);

        var progress = BossProgress.For(newestFirst, newestFirst[1], now);
        Assert.True(progress.HasData);
        Assert.Equal($"{"Kromede".ToUpper(CultureInfo.CurrentCulture)} · TU PROGRESO", progress.Title);
        Assert.Equal(3, progress.Bars.Count);
        Assert.Equal([200 / 300.0 * BossProgress.ChartHeight, 100 / 300.0 * BossProgress.ChartHeight, BossProgress.ChartHeight], progress.Bars.Select(b => b.Height));
        Assert.Equal("hace 3 semanas", progress.From);
        Assert.Equal("ayer", progress.To);
        Assert.Equal("1 / 3", progress.Kills);
        Assert.Equal("7", progress.Deaths);
        Assert.Equal(Numbers.Format(300), progress.Best);
        Assert.Equal(Numbers.Format(200), progress.Median);
        Assert.StartsWith("Kromede · 3 intentos", progress.Summary, StringComparison.Ordinal);

        // El filtro deja un solo boss: se grafica sin elegir fila.
        Assert.True(BossProgress.For(newestFirst[..3], null, now).HasData);

        // Una pelea sin nombre de objetivo no tiene con qué compararse.
        var unnamed = BossProgress.For(newestFirst, Item("e", "Encuentro", RunSegmentKind.Trash, 50), now);
        Assert.Equal(BossProgress.NoNameText, unnamed.EmptyText);
    }

    [Fact]
    public void Progress_draws_the_last_30_attempts()
    {
        var newestFirst = Enumerable.Range(0, 35).Select(i => Item($"{i}", "Kromede", RunSegmentKind.Wipe, 200 - i, started: Base.AddDays(-i))).ToList();

        var progress = BossProgress.For(newestFirst, newestFirst[0], Base);

        Assert.Equal(BossProgress.MaxBars, progress.Bars.Count);
        Assert.Equal(BossProgress.ChartHeight, progress.Bars[^1].Height);
        Assert.Equal("0 / 35", progress.Kills);
    }

    [Fact]
    public void Median_takes_the_middle_value()
    {
        Assert.Equal(0, BossProgress.MedianOf([]));
        Assert.Equal(2, BossProgress.MedianOf([3, 1, 2]));
        Assert.Equal(2.5, BossProgress.MedianOf([4, 1, 3, 2]));
    }

    [Fact]
    public void Dates_are_relative_to_today()
    {
        var now = new DateTimeOffset(new DateTime(2026, 10, 15, 20, 0, 0, DateTimeKind.Local));

        Assert.Equal("hoy", HistoryDates.Relative(now.AddHours(-3), now));
        Assert.Equal("ayer", HistoryDates.Relative(now.AddDays(-1), now));
        Assert.Equal("hace 5 días", HistoryDates.Relative(now.AddDays(-5), now));
        Assert.Equal("hace 3 semanas", HistoryDates.Relative(now.AddDays(-21), now));
        Assert.Equal("16/07/26", HistoryDates.Relative(now.AddDays(-91), now));
        Assert.Empty(HistoryDates.Relative(null, now));
    }

    [Fact]
    public void Saved_fights_get_the_dungeon_mode_badge()
    {
        var start = Base.ToString("O", CultureInfo.InvariantCulture);
        var end = Base.AddSeconds(95).ToString("O", CultureInfo.InvariantCulture);
        var wipe = new EncounterSummary("w", start, end, "Kromede", false, 1000, ActorsDamage: 1000, MainTargetDamage: 900, Deaths: 3);

        Assert.Equal(RunSegmentKind.Wipe, HistoryItem.From(wipe).Kind);
        Assert.Equal(RunSegmentKind.Kill, HistoryItem.From(wipe with { TargetKilled = true }).Kind);
        Assert.Equal(RunSegmentKind.Trash, HistoryItem.From(wipe with { MainTargetDamage = 100 }).Kind); // daño repartido
        Assert.Equal(RunSegmentKind.Trash, HistoryItem.From(wipe with { TargetName = null }).Kind);
        Assert.Equal(RunSegmentKind.Trash, HistoryItem.From(wipe with { TargetKilled = true, MainTargetSeconds = 12 }).Kind); // murió enseguida

        var item = HistoryItem.From(wipe with { Attempt = 3 });
        Assert.Equal(3, item.Deaths);
        Assert.Equal("✝ 3", item.DeathsText);
        Assert.Equal("intento 3", item.AttemptLabel);
        Assert.Equal("1:35", item.DurationText);
        Assert.Equal("—", item.LocalDpsText);
        Assert.Contains("3 muertes", item.RowTip, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_fight_row_is_marked_by_id()
    {
        var converter = new SameValueConverter();
        bool Same(params object?[] values) => (bool)converter.Convert(values!, typeof(bool), null!, CultureInfo.InvariantCulture);

        Assert.True(Same("a", "a"));
        Assert.False(Same("a", "b"));
        Assert.False(Same(null, null)); // sin pelea abierta no se marca ninguna
        Assert.False(Same("a", DependencyProperty.UnsetValue));
    }

    private static HistoryItem Item(
        string id, string target, RunSegmentKind kind, double dps, string? run = null, DateTimeOffset? started = null) =>
        new(id, target, "15/10 21:30", "01:35", "1k", kind == RunSegmentKind.Kill, LocalDps: dps, Run: run, Started: started ?? Base, Ended: started ?? Base,
            Character: "Vos", Seconds: 95, Kind: kind);

    /// <summary>Mide y acomoda sin mostrar la ventana: alcanza para expandir plantillas y resolver recursos.</summary>
    private static void Layout(Window window)
    {
        if (window.Content is FrameworkElement content)
        {
            content.Measure(new Size(1040, 700));
            content.Arrange(new Rect(0, 0, 1040, 700));
            content.UpdateLayout();
        }

        window.Close();
    }

    /// <summary>Espera una tarea bombeando la cola: los comandos async del VM vuelven por el Dispatcher.</summary>
    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Con este contexto, los await con ConfigureAwait(true) vuelven a este hilo y no al ThreadPool.
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
