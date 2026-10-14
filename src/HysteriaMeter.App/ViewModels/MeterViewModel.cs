using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Refresca el overlay leyendo snapshots del motor cada pocos milisegundos.</summary>
public sealed partial class MeterViewModel : ObservableObject
{
    private readonly CombatEngine _engine;
    private readonly DispatcherTimer _timer;
    private EncounterSnapshot? _lastFinished;

    public MeterViewModel(CombatEngine engine)
    {
        _engine = engine;
        _engine.EncounterFinished += snapshot => _lastFinished = snapshot;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher.CurrentDispatcher);
    }

    public ObservableCollection<ActorRowViewModel> Actors { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "Hysteria Meter";

    [ObservableProperty]
    public partial string Elapsed { get; set; } = "00:00";

    [ObservableProperty]
    public partial string TotalDps { get; set; } = "0";

    [ObservableProperty]
    public partial bool IsLive { get; set; }

    [ObservableProperty]
    public partial bool IsClickThrough { get; set; }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    [RelayCommand]
    private void Reset()
    {
        _engine.Reset();
        _lastFinished = null;
        Actors.Clear();
        Title = "Hysteria Meter";
        Elapsed = "00:00";
        TotalDps = "0";
    }

    private void Refresh()
    {
        var current = _engine.CurrentSnapshot();
        IsLive = current is not null;
        var snapshot = current ?? _lastFinished;
        if (snapshot is null)
        {
            return;
        }

        Title = snapshot.TargetName ?? "Encuentro";
        Elapsed = snapshot.Duration.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
        TotalDps = ActorRowViewModel.Compact(snapshot.TotalDamage / snapshot.Duration.TotalSeconds);

        var top = snapshot.Actors.Count > 0 ? snapshot.Actors[0].Damage : 0;
        for (var i = 0; i < snapshot.Actors.Count; i++)
        {
            if (i >= Actors.Count)
            {
                Actors.Add(new ActorRowViewModel());
            }

            Actors[i].Update(i + 1, snapshot.Actors[i], top);
        }

        while (Actors.Count > snapshot.Actors.Count)
        {
            Actors.RemoveAt(Actors.Count - 1);
        }
    }
}
