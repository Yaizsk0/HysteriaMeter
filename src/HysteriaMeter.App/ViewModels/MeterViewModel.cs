using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
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
    private readonly Action<MeterPreferences> _savePreferences;
    private MeterPreferences _preferences;
    private EncounterSnapshot? _lastFinished;

    public MeterViewModel(CombatEngine engine, MeterPreferences preferences, Action<MeterPreferences> savePreferences)
    {
        _engine = engine;
        _preferences = preferences;
        _savePreferences = savePreferences;
        _engine.Scope = preferences.Scope;
        _engine.SplitEncounters = preferences.SplitEncounters;
        _engine.EncounterFinished += snapshot => _lastFinished = snapshot;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher.CurrentDispatcher);
    }

    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    public bool ScopeMe
    {
        get => _preferences.Scope == ActorScope.Me;
        set => SetScope(value, ActorScope.Me);
    }

    public bool ScopeParty
    {
        get => _preferences.Scope == ActorScope.Party;
        set => SetScope(value, ActorScope.Party);
    }

    public bool ScopeEveryone
    {
        get => _preferences.Scope == ActorScope.Everyone;
        set => SetScope(value, ActorScope.Everyone);
    }

    public bool HealingHidden
    {
        get => _preferences.Healing == HealingView.Hidden;
        set => SetHealingView(value, HealingView.Hidden);
    }

    public bool HealingInTab
    {
        get => _preferences.Healing == HealingView.Tab;
        set => SetHealingView(value, HealingView.Tab);
    }

    public bool HealingCombined
    {
        get => _preferences.Healing == HealingView.Combined;
        set => SetHealingView(value, HealingView.Combined);
    }

    /// <summary>La pestaña CURACIÓN solo existe con la curación en su propia pestaña.</summary>
    public bool ShowHealingTab => _preferences.Healing == HealingView.Tab;

    /// <summary>Columna HPS al lado del DPS, en la pestaña de daño.</summary>
    public bool ShowHealingColumn => _preferences.Healing == HealingView.Combined && Tab == MeterTab.Damage;

    public bool SplitEncounters
    {
        get => _preferences.SplitEncounters;
        set
        {
            _engine.SplitEncounters = value;
            Update(_preferences with { SplitEncounters = value }, nameof(SplitEncounters));
        }
    }

    public string FontName
    {
        get => _preferences.TextFont;
        set => Update(_preferences with { TextFont = value }, nameof(FontName), nameof(Font));
    }

    public FontFamily Font => MeterPreferences.BundledFonts.Contains(_preferences.TextFont)
        ? new FontFamily(new Uri("pack://application:,,,/"), $"./Assets/Fonts/#{_preferences.TextFont}")
        : new FontFamily(_preferences.TextFont);

    public ObservableCollection<ActorRowViewModel> Actors { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = NoTarget;

    [ObservableProperty]
    public partial string Elapsed { get; set; } = "00:00";

    /// <summary>Vida del objetivo principal, si el servidor la mandó (spawn 0x3641 o actualización 0x8D00).</summary>
    [ObservableProperty]
    public partial bool HasTargetHealth { get; set; }

    [ObservableProperty]
    public partial string TargetHealthText { get; set; } = string.Empty;

    /// <summary>Proporción de vida restante (0..1) para la barra.</summary>
    [ObservableProperty]
    public partial double TargetHealthRatio { get; set; }

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
    [NotifyPropertyChangedFor(nameof(IsDamageTab), nameof(IsHealingTab), nameof(ColumnsHeader), nameof(RateUnit), nameof(ShowHealingColumn))]
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

    public string ColumnsHeader => ShowHealingColumn ? "DPS · HPS · %" : $"{RateUnit} · TOTAL · %";

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    partial void OnTabChanged(MeterTab value) => Refresh();

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    private void SetScope(bool selected, ActorScope scope)
    {
        if (!selected || _preferences.Scope == scope)
        {
            return;
        }

        // Cambiar el filtro a mitad de una pelea mezclaría criterios: se arranca de cero.
        _engine.Scope = scope;
        Reset();
        Update(_preferences with { Scope = scope }, nameof(ScopeMe), nameof(ScopeParty), nameof(ScopeEveryone));
    }

    private void SetHealingView(bool selected, HealingView view)
    {
        if (!selected || _preferences.Healing == view)
        {
            return;
        }

        Update(
            _preferences with { Healing = view },
            nameof(HealingHidden), nameof(HealingInTab), nameof(HealingCombined), nameof(ShowHealingTab), nameof(ShowHealingColumn), nameof(ColumnsHeader));
        if (view != HealingView.Tab)
        {
            Tab = MeterTab.Damage;
        }

        Refresh();
    }

    private void Update(MeterPreferences preferences, params string[] changed)
    {
        _preferences = preferences;
        _savePreferences(preferences);
        foreach (var name in changed)
        {
            OnPropertyChanged(name);
        }
    }

    [RelayCommand]
    private void Reset()
    {
        _engine.Reset();
        _lastFinished = null;
        Actors.Clear();
        Title = NoTarget;
        Elapsed = "00:00";
        HasTargetHealth = false;
        PartyPerSecond = "0";
        PartyTotal = "0";
        CritRate = "–";
        MaxHit = "–";
        IsEmpty = true;
    }

    private void UpdateTargetHealth(Health? health)
    {
        HasTargetHealth = health is { MaxHp: > 0 };
        if (health is not { MaxHp: > 0 })
        {
            return;
        }

        var ratio = Math.Clamp((double)health.Hp / health.MaxHp, 0, 1);
        TargetHealthRatio = ratio;
        TargetHealthText = $"{ActorRowViewModel.Compact(health.Hp)} / {ActorRowViewModel.Compact(health.MaxHp)} · {ratio.ToString("P0", CultureInfo.CurrentCulture)}";
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
        UpdateTargetHealth(snapshot.TargetHealth);

        Func<ActorSnapshot, long> metric = Tab == MeterTab.Healing ? a => a.Healing : a => a.Damage;
        var combined = ShowHealingColumn;
        var rows = snapshot.Actors
            .Where(a => metric(a) > 0 || (combined && a.Healing > 0))
            .OrderByDescending(metric)
            .ThenByDescending(a => a.Healing)
            .ToList();
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
            Actors[i].HealingPerSecond = combined && rows[i].Healing > 0
                ? ActorRowViewModel.Compact(rows[i].Healing / snapshot.Duration.TotalSeconds)
                : "–";
        }

        while (Actors.Count > rows.Count)
        {
            Actors.RemoveAt(Actors.Count - 1);
        }

        IsEmpty = Actors.Count == 0;
    }
}
