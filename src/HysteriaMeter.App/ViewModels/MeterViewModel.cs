using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;

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
    private readonly CaptureRecorder _recorder;
    private readonly EncounterRepository? _history;
    private readonly DispatcherTimer _timer;
    private readonly Action<MeterPreferences> _savePreferences;
    private MeterPreferences _preferences;
    private EncounterSnapshot? _lastFinished;

    // Encuentro del historial que se está mirando; mientras tanto el vivo sigue acumulando por detrás.
    private EncounterSnapshot? _pinned;

    public MeterViewModel(
        CombatEngine engine,
        CaptureRecorder recorder,
        SkillNames skillNames,
        MeterPreferences preferences,
        Action<MeterPreferences> savePreferences,
        EncounterRepository? history = null)
    {
        _history = history;
        Detail = new ActorDetailViewModel(skillNames);
        _engine = engine;
        _recorder = recorder;
        _preferences = preferences;
        _savePreferences = savePreferences;
        _engine.Scope = preferences.Scope;
        _engine.SplitEncounters = preferences.SplitEncounters;
        _engine.EncounterFinished += snapshot => _lastFinished = snapshot;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher.CurrentDispatcher);
    }

    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    [ObservableProperty]
    public partial bool IsHistoryOpen { get; set; }

    /// <summary>Encuentros guardados, del más nuevo al más viejo. Se cargan al abrir el panel.</summary>
    public ObservableCollection<HistoryItem> History { get; } = [];

    /// <summary>Se está mirando un encuentro guardado en vez del vivo.</summary>
    [ObservableProperty]
    public partial bool IsViewingHistory { get; set; }

    [ObservableProperty]
    public partial string HistoryLabel { get; set; } = string.Empty;

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

    /// <summary>Graba lo capturado en un .pcap. No se guarda entre sesiones: cada vez arranca apagado.</summary>
    public bool IsRecording
    {
        get => _recorder.CurrentPath is not null;
        set
        {
            if (value == IsRecording)
            {
                return;
            }

            if (value)
            {
                _recorder.Start(DateTime.Now);
            }
            else
            {
                _recorder.Stop();
            }

            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(RecordingHint));
        }
    }

    public string RecordingHint => _recorder.CurrentPath is { } path
        ? $"Grabando en {Path.GetFileName(path)}"
        : "Para comparar una pelea con Combat Analysis";

    public string FontName
    {
        get => _preferences.TextFont;
        set => Update(_preferences with { TextFont = value }, nameof(FontName), nameof(Font));
    }

    public FontFamily Font => MeterPreferences.BundledFonts.Contains(_preferences.TextFont)
        ? new FontFamily(new Uri("pack://application:,,,/"), $"./Assets/Fonts/#{_preferences.TextFont}")
        : new FontFamily(_preferences.TextFont);

    public ObservableCollection<ActorRowViewModel> Actors { get; } = [];

    /// <summary>Desglose por skill del jugador en el que se hizo clic.</summary>
    public ActorDetailViewModel Detail { get; }

    /// <summary>La vista abre (o trae al frente) la ventana de detalle.</summary>
    public event Action? DetailRequested;

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
    private static void OpenRecordings()
    {
        Directory.CreateDirectory(CaptureRecorder.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{CaptureRecorder.Folder}\"") { UseShellExecute = true });
    }

    /// <summary>Último encuentro mostrado, para copiar el resumen.</summary>
    private EncounterSnapshot? _shown;

    /// <summary>Copia un resumen corto, apto para pegar en el chat del juego.</summary>
    [RelayCommand]
    private void CopySummary()
    {
        if (_shown is { } snapshot && BuildSummary(snapshot, Tab) is { Length: > 0 } text)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Otro programa tiene el portapapeles abierto: se puede volver a intentar.
            }
        }
    }

    /// <summary>"Boss 02:47 · 1. Ana 11.9k (52%) · 2. Beto 7.8k (34%)". Máximo 8 jugadores para que entre en el chat.</summary>
    public static string BuildSummary(EncounterSnapshot snapshot, MeterTab tab)
    {
        Func<ActorSnapshot, long> metric = tab == MeterTab.Healing ? a => a.Healing : a => a.Damage;
        var seconds = snapshot.Duration.TotalSeconds;
        var rows = snapshot.Actors.Where(a => metric(a) > 0).OrderByDescending(metric).Take(8).ToList();
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var total = (double)snapshot.Actors.Sum(metric);
        var unit = tab == MeterTab.Healing ? "HPS" : "DPS";
        var header = $"{snapshot.TargetName ?? "Encuentro"} {snapshot.Duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture)} {unit}";
        var parts = rows.Select((a, i) =>
            $"{i + 1}. {a.Name} {ActorRowViewModel.Compact(metric(a) / seconds)} ({(metric(a) / total).ToString("P0", CultureInfo.InvariantCulture).Replace(" ", string.Empty, StringComparison.Ordinal)})");
        return string.Join(" · ", parts.Prepend(header));
    }

    [RelayCommand]
    private void ShowDetail(ActorRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Detail.Select(row);
        Refresh();
        DetailRequested?.Invoke();
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
        IsHistoryOpen &= !IsSettingsOpen;
    }

    [RelayCommand]
    private async Task ToggleHistoryAsync()
    {
        IsHistoryOpen = !IsHistoryOpen;
        IsSettingsOpen &= !IsHistoryOpen;
        if (!IsHistoryOpen || _history is null)
        {
            return;
        }

        History.Clear();
        try
        {
            foreach (var summary in await _history.ListRecentAsync(40).ConfigureAwait(true))
            {
                History.Add(HistoryItem.From(summary));
            }
        }
        catch (DbException)
        {
            // La base todavía no está lista (la inicializa el pipeline al arrancar): el panel queda vacío.
        }
    }

    [RelayCommand]
    private async Task OpenEncounterAsync(HistoryItem? item)
    {
        if (item is null || _history is null)
        {
            return;
        }

        EncounterSnapshot? snapshot;
        try
        {
            snapshot = await _history.LoadAsync(item.Id).ConfigureAwait(true);
        }
        catch (DbException)
        {
            return;
        }

        if (snapshot is null)
        {
            return;
        }

        _pinned = snapshot;
        IsViewingHistory = true;
        HistoryLabel = $"HISTORIAL · {item.When}";
        IsHistoryOpen = false;
        Refresh();
    }

    [RelayCommand]
    private void BackToLive()
    {
        _pinned = null;
        IsViewingHistory = false;
        Refresh();
    }

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
        _pinned = null;
        _shown = null;
        IsViewingHistory = false;
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
        IsLive = _pinned is null && current is not null;
        var snapshot = _pinned ?? current ?? _lastFinished;
        if (snapshot is null)
        {
            return;
        }

        _shown = snapshot;
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
        if (Detail.ActorId is not null)
        {
            Detail.Update(snapshot);
        }
    }
}
