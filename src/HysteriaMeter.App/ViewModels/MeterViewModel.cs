using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Pestañas del overlay. "Recibido" todavía no tiene datos: falta decodificar el daño a jugadores.</summary>
public enum MeterTab
{
    Damage,
    Healing,
}

/// <summary>Refresca el overlay leyendo snapshots del motor cada pocos milisegundos.</summary>
public sealed partial class MeterViewModel : ObservableObject
{
    private const string NoTarget = "Esperando combate";

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
    public partial string Title { get; set; } = NoTarget;

    [ObservableProperty]
    public partial string Elapsed { get; set; } = "00:00";

    /// <summary>DPS (o HPS) y total de toda la party, para el pie.</summary>
    [ObservableProperty]
    public partial string PartyPerSecond { get; set; } = "0";

    [ObservableProperty]
    public partial string PartyTotal { get; set; } = "0";

    /// <summary>Crítico y golpe máximo del jugador local (o del primero si todavía no lo detectamos).</summary>
    [ObservableProperty]
    public partial string CritRate { get; set; } = "–";

    [ObservableProperty]
    public partial string MaxHit { get; set; } = "–";

    [ObservableProperty]
    public partial bool IsLive { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial bool IsClickThrough { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDamageTab), nameof(IsHealingTab), nameof(ColumnsHeader), nameof(RateUnit))]
    public partial MeterTab Tab { get; set; }

    public bool IsDamageTab
    {
        get => Tab == MeterTab.Damage;
        set
        {
            if (value)
            {
                Tab = MeterTab.Damage;
            }
        }
    }

    public bool IsHealingTab
    {
        get => Tab == MeterTab.Healing;
        set
        {
            if (value)
            {
                Tab = MeterTab.Healing;
            }
        }
    }

    public string RateUnit => Tab == MeterTab.Healing ? "HPS" : "DPS";

    public string ColumnsHeader => $"{RateUnit} · TOTAL · %";

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    partial void OnTabChanged(MeterTab value) => Refresh();

    [RelayCommand]
    private void Reset()
    {
        _engine.Reset();
        _lastFinished = null;
        Actors.Clear();
        Title = NoTarget;
        Elapsed = "00:00";
        PartyPerSecond = "0";
        PartyTotal = "0";
        CritRate = "–";
        MaxHit = "–";
        IsEmpty = true;
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
        Elapsed = snapshot.Duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

        Func<ActorSnapshot, long> metric = Tab == MeterTab.Healing ? a => a.Healing : a => a.Damage;
        var rows = snapshot.Actors.Where(a => metric(a) > 0).OrderByDescending(metric).ToList();
        var total = rows.Sum(metric);
        var top = rows.Count > 0 ? metric(rows[0]) : 0;

        PartyTotal = ActorRowViewModel.Compact(total);
        PartyPerSecond = ActorRowViewModel.Compact(total / snapshot.Duration.TotalSeconds);

        var focus = snapshot.Actors.FirstOrDefault(a => a.IsLocalPlayer) ?? (snapshot.Actors.Count > 0 ? snapshot.Actors[0] : null);
        CritRate = focus is null ? "–" : focus.CritRate.ToString("P0", CultureInfo.CurrentCulture);
        MaxHit = focus is null ? "–" : ActorRowViewModel.Compact(focus.MaxHit);

        for (var i = 0; i < rows.Count; i++)
        {
            if (i >= Actors.Count)
            {
                Actors.Add(new ActorRowViewModel());
            }

            Actors[i].Update(i + 1, rows[i], Tab, top, total, snapshot.Duration);
        }

        while (Actors.Count > rows.Count)
        {
            Actors.RemoveAt(Actors.Count - 1);
        }

        IsEmpty = Actors.Count == 0;
    }
}
