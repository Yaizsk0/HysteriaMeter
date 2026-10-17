using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;
using Microsoft.Data.Sqlite;

namespace HysteriaMeter.App.Tests;

[Collection(WpfAppGroup.Name)]
public sealed class HistoryResumeTests
{
    private static readonly string[] DatabaseSuffixes = ["", "-wal", "-shm"];
    [Fact]
    public void Changing_fight_saves_the_note_being_edited()
    {
        WithHistory((vm, repository, fights) =>
        {
            Pump(vm.ToggleHistoryCommand.ExecuteAsync(null));
            var items = vm.HistoryRuns.SelectMany(r => r.Fights).ToArray();
            Pump(vm.OpenEncounterCommand.ExecuteAsync(items[0]));
            vm.CurrentNote = "  cambié la rotación  ";
            Pump(vm.OpenEncounterCommand.ExecuteAsync(items[1]));
            var saved = Pump(repository.ListRecentAsync());
            Assert.Equal("cambié la rotación", saved.Single(i => i.Id == items[0].Id).Note);
            Assert.Empty(vm.CurrentNote);
        });
    }

    [Fact]
    public void Kill_filter_keeps_wipes_in_the_boss_progress()
    {
        WithHistory((vm, repository, fights) =>
        {
            Pump(vm.ToggleHistoryCommand.ExecuteAsync(null));
            vm.OnlyKills = true;
            Assert.Single(vm.HistoryRuns.SelectMany(r => r.Fights));
            Assert.True(vm.HasProgress);
            Assert.Equal("1 / 3", vm.Progress.Kills);
            Assert.Equal(3, vm.Progress.Bars.Count);
        });
    }

    [Fact]
    public void Deleting_a_fight_recalculates_attempts_and_percentiles()
    {
        WithHistory((vm, repository, fights) =>
        {
            Pump(vm.ToggleHistoryCommand.ExecuteAsync(null));
            var newest = vm.HistoryRuns.SelectMany(r => r.Fights).First();
            Pump(vm.OpenEncounterCommand.ExecuteAsync(newest));
            Pump(vm.DeleteEncounterCommand.ExecuteAsync(null));
            var remaining = vm.HistoryRuns.SelectMany(r => r.Fights).ToArray();
            Assert.Equal(2, remaining.Length);
            Assert.Equal(2, remaining[0].Attempt);
            Assert.Equal(100, remaining[0].Percentile);
            Assert.True(remaining[0].IsRecord);
        });
    }

    [Fact]
    public void Saving_a_fight_refreshes_an_open_history_after_commit()
    {
        WithHistory((vm, repository, fights) =>
        {
            vm.Start();
            Pump(vm.ToggleHistoryCommand.ExecuteAsync(null));
            var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MeterViewModel.HistoryCountText) && vm.HistoryCountText.StartsWith("4 peleas", StringComparison.Ordinal))
                {
                    refreshed.TrySetResult();
                }
            };
            Pump(repository.SaveAsync(fights[^1] with { Id = Guid.NewGuid(), Start = fights[^1].Start.AddMinutes(10), End = fights[^1].End.AddMinutes(10) }));
            Pump(refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(4, vm.HistoryRuns.SelectMany(r => r.Fights).Count());
        });
    }
    [Fact]
    public void Minimizing_or_blocking_closes_the_open_history()
    {
        WithHistory((vm, repository, fights) =>
        {
            Pump(vm.ToggleHistoryCommand.ExecuteAsync(null));
            vm.IsMinimized = true;
            Assert.False(vm.IsHistoryOpen);
            vm.IsMinimized = false;
            Pump(vm.ToggleHistoryCommand.ExecuteAsync(null));
            vm.IsClickThrough = true;
            Assert.False(vm.IsHistoryOpen);
        });
    }
    [Fact]
    public void Moving_newer_than_the_latest_fight_saves_its_edited_note()
    {
        WithHistory((vm, repository, fights) =>
        {
            Pump(vm.ToggleHistoryCommand.ExecuteAsync(null));
            var newest = vm.HistoryRuns.SelectMany(r => r.Fights).First();
            Pump(vm.OpenEncounterCommand.ExecuteAsync(newest));
            vm.CurrentNote = "  nota antes de volver al vivo  ";
            Pump(vm.NewerFightCommand.ExecuteAsync(null));
            Assert.False(vm.IsViewingHistory);
            Assert.Null(vm.PinnedHistoryId);
            var saved = Pump(repository.ListRecentAsync());
            Assert.Equal("nota antes de volver al vivo", saved.Single(i => i.Id == newest.Id).Note);
        });
    }
    private static void WithHistory(Action<MeterViewModel, EncounterRepository, EncounterSnapshot[]> action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            var database = Path.Combine(Path.GetTempPath(), $"hysteria-history-resume-{Guid.NewGuid():N}.db");
            MeterViewModel? vm = null;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                var repository = new EncounterRepository(database);
                Pump(repository.InitializeAsync());
                var start = new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.Zero);
                var fights = Enumerable.Range(1, 3).Select(n =>
                {
                    var fight = DemoEncounter.Create(start.AddMinutes(n * 10));
                    return fight with
                    {
                        Id = Guid.NewGuid(), TargetKilled = n == 3, ZoneEnteredAt = start,
                        Actors = [.. fight.Actors.Select(a => a.IsLocalPlayer ? a with { Dps = n * 100 } : a)],
                    };
                }).ToArray();
                foreach (var fight in fights) { Pump(repository.SaveAsync(fight)); }
                vm = new MeterViewModel(new MeterSession(new CombatEngine(), new CaptureRecorder(), SkillNames.Empty,
                    new MeterPreferences { LastSeenNews = 1 }, _ => { }, history: repository));
                action(vm, repository, fights);
            }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
            finally
            {
                vm?.Stop();
                SqliteConnection.ClearAllPools();
                foreach (var suffix in DatabaseSuffixes) { File.Delete(database + suffix); }
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

    private static T Pump<T>(Task<T> task)
    {
        Pump((Task)task);
        return task.GetAwaiter().GetResult();
    }
}
