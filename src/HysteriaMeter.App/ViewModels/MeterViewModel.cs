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

/// <summary>Pestañas del overlay.</summary>
public enum MeterTab
{
    Damage,
    Healing,

    /// <summary>Daño recibido de monstruos.</summary>
    Taken,
}

public static class MeterTabs
{
    /// <summary>Lo que mide cada pestaña.</summary>
    public static long Metric(this MeterTab tab, ActorSnapshot actor) => tab switch
    {
        MeterTab.Healing => actor.Healing,
        MeterTab.Taken => actor.DamageTaken,
        _ => actor.Damage,
    };

    public static string Unit(this MeterTab tab) => tab switch
    {
        MeterTab.Healing => "HPS",
        MeterTab.Taken => "DTPS",
        _ => "DPS",
    };
}

/// <summary>Refresca el overlay leyendo snapshots del motor cada pocos milisegundos.</summary>
/// <summary>Una opción del selector de formato de números: "2.04M", "2.0M" o "2.040.321".</summary>
public sealed record NumberStyleOption(NumberStyle Style, string Example);

public sealed partial class MeterViewModel : ObservableObject
{
    private const string NoTarget = "Esperando combate";

    /// <summary>Sin combate durante este tiempo, el overlay se atenúa (si la opción está activa).</summary>
    public static readonly TimeSpan FadeAfter = TimeSpan.FromSeconds(20);

    /// <summary>Opacidad del overlay atenuado.</summary>
    public const double FadedOpacity = 0.2;

    private readonly MeterSession _session;
    private readonly CombatEngine _engine;
    private readonly CaptureRecorder _recorder;
    private readonly EncounterRepository? _history;
    private readonly DispatcherTimer _timer;
    private EncounterSnapshot? _lastFinished;

    // Encuentro del historial que se está mirando; mientras tanto el vivo sigue acumulando por detrás.
    private EncounterSnapshot? _pinned;

    /// <param name="tab">Qué muestra esta ventana al abrirse.</param>
    /// <param name="showHealing">Columna HPS al lado del DPS en esta ventana.</param>
    /// <param name="compact">Modo compacto: solo las filas y el reloj.</param>
    public MeterViewModel(MeterSession session, MeterTab tab = MeterTab.Damage, bool showHealing = true, bool compact = false)
    {
        _session = session;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(session.Preferences.RefreshMs), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher.CurrentDispatcher);
        _engine = session.Engine;
        _recorder = session.Recorder;
        _history = session.History;
        Detail = new ActorDetailViewModel(session.SkillNames) { LogSource = session.Engine.CombatLog };
        Tab = tab;
        _showHealing = showHealing;
        IsCompact = compact;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _engine.EncounterFinished += snapshot =>
        {
            _lastFinished = snapshot;
            dispatcher.InvokeAsync(() => CheckRecordAsync(snapshot));
        };
        _session.PreferencesChanged += names =>
        {
            foreach (var name in names)
            {
                OnPropertyChanged(name);
            }

            _timer.Interval = TimeSpan.FromMilliseconds(Preferences.RefreshMs);
            if (names.Contains(nameof(SelectedNumberStyle)) || names.Contains(nameof(ShowSparklines)))
            {
                Refresh();
            }
        };
        _session.ResetRequested += ClearRows;
        _session.DiscordResult += message => dispatcher.InvokeAsync(() => ExportMessage = message);
        _recorder.Changed += _ =>
        {
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(RecordingHint));
        };
    }

    private MeterPreferences Preferences => _session.Preferences;

    private bool _showHealing;

    /// <summary>La ventana pide abrir otra (botón +).</summary>
    public event Action? NewWindowRequested;

    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    [ObservableProperty]
    public partial bool IsHistoryOpen { get; set; }

    /// <summary>Encuentros guardados que pasan el filtro, del más nuevo al más viejo. Se cargan al abrir el panel.</summary>
    public ObservableCollection<HistoryItem> History { get; } = [];

    private List<HistoryItem> _allHistory = [];

    /// <summary>Búsqueda del panel de historial: objetivo o nota.</summary>
    [ObservableProperty]
    public partial string HistorySearch { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool OnlyKills { get; set; }

    partial void OnHistorySearchChanged(string value) => ApplyHistoryFilter();

    partial void OnOnlyKillsChanged(bool value) => ApplyHistoryFilter();

    // Pelea guardada que se está mirando (para la nota y para borrarla).
    private string? _pinnedId;

    /// <summary>Nota de la pelea guardada que se está mirando; se guarda al salir del cuadro de texto.</summary>
    [ObservableProperty]
    public partial string CurrentNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanEditNote { get; set; }

    /// <summary>Aviso de récord personal al terminar una pelea.</summary>
    [ObservableProperty]
    public partial string RecordMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRecordMessage { get; set; }

    private DateTime _recordUntilUtc;

    /// <summary>Cuánto queda visible el aviso de récord.</summary>
    public static readonly TimeSpan RecordNoticeDuration = TimeSpan.FromSeconds(15);

    /// <summary>Opciones del selector de formato, con un ejemplo de cada una.</summary>
    public static IReadOnlyList<NumberStyleOption> NumberStyleOptions { get; } =
        [.. Enum.GetValues<NumberStyle>().Select(style => new NumberStyleOption(style, Numbers.Example(style)))];

    public NumberStyleOption SelectedNumberStyle
    {
        get => NumberStyleOptions.First(o => o.Style == Preferences.Numbers);
        set => Update(Preferences with { Numbers = value.Style }, nameof(SelectedNumberStyle));
    }

    public int RefreshMs
    {
        get => Preferences.RefreshMs;
        set => Update(Preferences with { RefreshMs = value }, nameof(RefreshMs));
    }

    public bool ShowSparklines
    {
        get => Preferences.Sparklines;
        set => Update(Preferences with { Sparklines = value }, nameof(ShowSparklines));
    }

    public int RetentionDays
    {
        get => Preferences.RetentionDays;
        set
        {
            Update(Preferences with { RetentionDays = value }, nameof(RetentionDays));
            _ = CleanupHistoryAsync();
        }
    }

    /// <summary>Se está mirando un encuentro guardado en vez del vivo.</summary>
    [ObservableProperty]
    public partial bool IsViewingHistory { get; set; }

    [ObservableProperty]
    public partial string HistoryLabel { get; set; } = string.Empty;

    public bool ScopeMe
    {
        get => Preferences.Scope == ActorScope.Me;
        set => SetScope(value, ActorScope.Me);
    }

    public bool ScopeParty
    {
        get => Preferences.Scope == ActorScope.Party;
        set => SetScope(value, ActorScope.Party);
    }

    public bool ScopeEveryone
    {
        get => Preferences.Scope == ActorScope.Everyone;
        set => SetScope(value, ActorScope.Everyone);
    }

    /// <summary>Opción de esta ventana: columna HPS al lado del DPS en la pestaña de daño.</summary>
    public bool ShowHealing
    {
        get => _showHealing;
        set
        {
            if (SetProperty(ref _showHealing, value))
            {
                OnPropertyChanged(nameof(ShowHealingColumn));
                OnPropertyChanged(nameof(SecondColumnHeader));
                Refresh();
            }
        }
    }

    /// <summary>Columna HPS al lado del DPS, en la pestaña de daño.</summary>
    public bool ShowHealingColumn => _showHealing && Tab == MeterTab.Damage;

    public bool SplitEncounters
    {
        get => Preferences.SplitEncounters;
        set
        {
            _engine.SplitEncounters = value;
            Update(Preferences with { SplitEncounters = value }, nameof(SplitEncounters));
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
        }
    }

    /// <summary>Versión y commit del build, en configuración y en el tooltip del título.</summary>
#pragma warning disable CA1822 // propiedad de instancia para el binding de WPF
    public string Version => AppVersion.Text;
#pragma warning restore CA1822

    public string RecordingHint => _recorder.CurrentPath is { } path
        ? $"Grabando en {Path.GetFileName(path)}"
        : "Para comparar una pelea con Combat Analysis";

    /// <summary>Opacidad elegida para el overlay (una de <see cref="MeterPreferences.Opacities"/>).</summary>
    public double OverlayOpacity
    {
        get => Preferences.Opacity;
        set => Update(Preferences with { Opacity = Math.Clamp(value, 0.3, 1) }, nameof(OverlayOpacity), nameof(EffectiveOpacity));
    }

    public bool OpaqueOnHover
    {
        get => Preferences.OpaqueOnHover;
        set => Update(Preferences with { OpaqueOnHover = value }, nameof(OpaqueOnHover), nameof(EffectiveOpacity));
    }

    public bool FadeWhenIdle
    {
        get => Preferences.FadeWhenIdle;
        set => Update(Preferences with { FadeWhenIdle = value }, nameof(FadeWhenIdle), nameof(EffectiveOpacity));
    }

    /// <summary>El mouse está sobre la ventana (lo avisa la vista).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveOpacity))]
    public partial bool IsHovered { get; set; }

    private DateTime _lastCombatUtc = DateTime.UtcNow;

    /// <summary>Opacidad que se aplica a la ventana: la elegida, opaca con el mouse encima, atenuada sin combate.</summary>
    public double EffectiveOpacity =>
        ComputeOpacity(Preferences.Opacity, IsHovered, Preferences.OpaqueOnHover, Preferences.FadeWhenIdle, DateTime.UtcNow - _lastCombatUtc);

    public static double ComputeOpacity(double chosen, bool hovered, bool opaqueOnHover, bool fadeWhenIdle, TimeSpan sinceCombat)
    {
        if (hovered && opaqueOnHover)
        {
            return 1;
        }

        return fadeWhenIdle && sinceCombat > FadeAfter ? Math.Min(chosen, FadedOpacity) : chosen;
    }

    /// <summary>Modo compacto de esta ventana: sin objetivo, pestañas, encabezados ni pie.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpanded))]
    public partial bool IsCompact { get; set; }

    public bool IsExpanded => !IsCompact;

    /// <summary>Estado de la captura: "live", "idle", "warn" o "error" (color del punto del encabezado).</summary>
    [ObservableProperty]
    public partial string StatusKind { get; set; } = "idle";

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>Lo que se muestra con el overlay vacío: espera de combate o el problema de captura.</summary>
    [ObservableProperty]
    public partial string EmptyText { get; set; } = "Esperando el primer golpe…";

    public string FontName
    {
        get => Preferences.TextFont;
        set => Update(Preferences with { TextFont = value }, nameof(FontName), nameof(Font));
    }

    public FontFamily Font => MeterPreferences.BundledFonts.Contains(Preferences.TextFont)
        ? new FontFamily(new Uri("pack://application:,,,/"), $"./Assets/Fonts/#{Preferences.TextFont}")
        : new FontFamily(Preferences.TextFont);

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
    [NotifyPropertyChangedFor(nameof(IsDamageTab), nameof(IsHealingTab), nameof(IsTakenTab), nameof(SecondColumnHeader), nameof(RateUnit), nameof(ShowHealingColumn))]
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

    public bool IsTakenTab
    {
        get => Tab == MeterTab.Taken;
        set
        {
            if (value)
            {
                Tab = MeterTab.Taken;
            }
        }
    }

    public string RateUnit => Tab.Unit();

    /// <summary>Encabezado de la segunda columna numérica: HPS con daño y curación juntos, si no el total.</summary>
    public string SecondColumnHeader => ShowHealingColumn ? "HPS" : "TOTAL";

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
        if (_shown is { } snapshot && BuildSummary(_session.ForSharing(snapshot), Tab) is { Length: > 0 } text)
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
        Func<ActorSnapshot, long> metric = a => tab.Metric(a);
        var seconds = snapshot.Duration.TotalSeconds;
        var rows = snapshot.Actors.Where(a => metric(a) > 0).OrderByDescending(metric).Take(8).ToList();
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var total = (double)snapshot.Actors.Sum(metric);
        var unit = tab.Unit();
        var header = $"{snapshot.TargetName ?? "Encuentro"} {snapshot.Duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture)} {unit}";
        var parts = rows.Select((a, i) =>
            $"{i + 1}. {a.Name} {ActorRowViewModel.Compact(metric(a) / seconds)} ({(metric(a) / total).ToString("P0", CultureInfo.InvariantCulture).Replace(" ", string.Empty, StringComparison.Ordinal)})");
        return string.Join(" · ", parts.Prepend(header));
    }

    /// <summary>Carpeta de los CSV y JSON exportados.</summary>
    public static string ExportFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HysteriaMeter", "Exportes");

    /// <summary>Resultado de la última exportación o envío, en configuración.</summary>
    [ObservableProperty]
    public partial string ExportMessage { get; set; } = string.Empty;

    public bool AnonymizeExports
    {
        get => Preferences.AnonymizeExports;
        set => Update(Preferences with { AnonymizeExports = value }, nameof(AnonymizeExports));
    }

    public string DiscordWebhookUrl
    {
        get => Preferences.DiscordWebhook;
        set => Update(Preferences with { DiscordWebhook = value?.Trim() ?? string.Empty }, nameof(DiscordWebhookUrl), nameof(IsDiscordConfigured));
    }

    public bool IsDiscordConfigured => DiscordWebhook.IsValidUrl(Preferences.DiscordWebhook);

    public bool DiscordOnKill
    {
        get => Preferences.DiscordOnKill;
        set => Update(Preferences with { DiscordOnKill = value }, nameof(DiscordOnKill));
    }

    /// <summary>La vista copia la imagen del overlay al portapapeles.</summary>
    public event Action? CopyImageRequested;

    [RelayCommand]
    private void ExportCsv() => Export("csv", EncounterExport.ToCsv);

    [RelayCommand]
    private void ExportJson() => Export("json", EncounterExport.ToJson);

    private void Export(string extension, Func<EncounterSnapshot, string> serialize)
    {
        if (_shown is not { } snapshot)
        {
            ExportMessage = "Todavía no hay una pelea para exportar";
            return;
        }

        try
        {
            Directory.CreateDirectory(ExportFolder);
            var path = Path.Combine(ExportFolder, $"{EncounterExport.FileName(snapshot, snapshot.Start.ToLocalTime())}.{extension}");
            File.WriteAllText(path, serialize(_session.ForSharing(snapshot)), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            ExportMessage = $"Guardado {Path.GetFileName(path)}";
        }
        catch (IOException ex)
        {
            ExportMessage = $"No se pudo guardar: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            ExportMessage = $"No se pudo guardar: {ex.Message}";
        }
    }

    [RelayCommand]
    private static void OpenExports()
    {
        Directory.CreateDirectory(ExportFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ExportFolder}\"") { UseShellExecute = true });
    }

    /// <summary>Cierra los paneles para que la imagen muestre las filas y le pide a la vista que la copie.</summary>
    [RelayCommand]
    private void CopyImage()
    {
        IsSettingsOpen = false;
        IsHistoryOpen = false;
        CopyImageRequested?.Invoke();
    }

    [RelayCommand]
    private async Task SendToDiscordAsync()
    {
        if (_shown is not { } snapshot)
        {
            ExportMessage = "Todavía no hay una pelea para mandar";
            return;
        }

        ExportMessage = "Mandando a Discord…";
        await _session.SendToDiscordAsync(snapshot).ConfigureAwait(true);
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

        try
        {
            _allHistory = [.. (await _history.ListRecentAsync(200).ConfigureAwait(true)).Select(HistoryItem.From)];
        }
        catch (DbException)
        {
            // La base todavía no está lista (la inicializa el pipeline al arrancar): el panel queda vacío.
            _allHistory = [];
        }

        ApplyHistoryFilter();
    }

    private void ApplyHistoryFilter()
    {
        History.Clear();
        foreach (var item in _allHistory.Where(i => i.Matches(HistorySearch, OnlyKills)).Take(60))
        {
            History.Add(item);
        }
    }

    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (_pinnedId is null || _history is null)
        {
            return;
        }

        try
        {
            await _history.SetNoteAsync(_pinnedId, CurrentNote).ConfigureAwait(true);
            var index = _allHistory.FindIndex(i => i.Id == _pinnedId);
            if (index >= 0)
            {
                _allHistory[index] = _allHistory[index] with { Note = string.IsNullOrWhiteSpace(CurrentNote) ? null : CurrentNote.Trim() };
            }
        }
        catch (DbException)
        {
        }
    }

    /// <summary>Borra la pelea guardada que se está mirando y vuelve al vivo.</summary>
    [RelayCommand]
    private async Task DeleteEncounterAsync()
    {
        if (_pinnedId is null || _history is null)
        {
            return;
        }

        try
        {
            await _history.DeleteAsync(_pinnedId).ConfigureAwait(true);
            _allHistory.RemoveAll(i => i.Id == _pinnedId);
        }
        catch (DbException)
        {
            return;
        }

        BackToLive();
    }

    private async Task CleanupHistoryAsync()
    {
        try
        {
            await _session.CleanupHistoryAsync(DateTimeOffset.UtcNow).ConfigureAwait(true);
        }
        catch (DbException)
        {
        }
    }

    /// <summary>Al terminar una pelea, avisa si el DPS propio superó el mejor contra ese objetivo.</summary>
    private async Task CheckRecordAsync(EncounterSnapshot snapshot)
    {
        var local = snapshot.Actors.FirstOrDefault(a => a.IsLocalPlayer);
        if (_history is null || local is null || snapshot.TargetName is not { Length: > 0 } target)
        {
            return;
        }

        double? best;
        try
        {
            best = await _history.BestLocalDpsAsync(target, snapshot.Id.ToString()).ConfigureAwait(true);
        }
        catch (DbException)
        {
            return;
        }

        if (RecordText(target, local.Dps, best) is { } message)
        {
            RecordMessage = message;
            HasRecordMessage = true;
            _recordUntilUtc = DateTime.UtcNow + RecordNoticeDuration;
        }
    }

    /// <summary>"¡Récord en Tarka! 12.4k DPS (antes 11.0k)", o <c>null</c> si no lo superó o es la primera vez.</summary>
    public static string? RecordText(string target, double dps, double? previousBest) =>
        previousBest is { } best && dps > best
            ? $"¡Récord en {target}! {ActorRowViewModel.Compact(dps)} DPS (antes {ActorRowViewModel.Compact(best)})"
            : null;

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
        _pinnedId = item.Id;
        CurrentNote = item.Note ?? string.Empty;
        CanEditNote = true;
        IsViewingHistory = true;
        HistoryLabel = $"HISTORIAL · {item.When}";
        IsHistoryOpen = false;
        Refresh();
    }

    /// <summary>Muestra una pelea inventada para acomodar el overlay sin combatir.</summary>
    [RelayCommand]
    private void ShowDemo()
    {
        _pinned = DemoEncounter.Create(DateTimeOffset.Now);
        _pinnedId = null;
        CanEditNote = false;
        IsViewingHistory = true;
        HistoryLabel = "DEMO · datos de prueba";
        IsSettingsOpen = false;
        IsHistoryOpen = false;
        Refresh();
    }

    [RelayCommand]
    private void ToggleCompact() => IsCompact = !IsCompact;

    [RelayCommand]
    private void BackToLive()
    {
        _pinned = null;
        _pinnedId = null;
        CanEditNote = false;
        IsViewingHistory = false;
        Refresh();
    }

    private void SetScope(bool selected, ActorScope scope)
    {
        if (!selected || Preferences.Scope == scope)
        {
            return;
        }

        // Cambiar el filtro a mitad de una pelea mezclaría criterios: se arranca de cero.
        _engine.Scope = scope;
        _session.Reset();
        Update(Preferences with { Scope = scope }, nameof(ScopeMe), nameof(ScopeParty), nameof(ScopeEveryone));
    }

    private void Update(MeterPreferences preferences, params string[] changed) => _session.Update(preferences, changed);

    [RelayCommand]
    private void NewWindow() => NewWindowRequested?.Invoke();

    /// <summary>Reinicia el encuentro en todas las ventanas.</summary>
    [RelayCommand]
    private void Reset() => _session.Reset();

    private void ClearRows()
    {
        _lastFinished = null;
        _pinned = null;
        _pinnedId = null;
        CanEditNote = false;
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

    private void UpdateStatus(bool inCombat)
    {
        var now = DateTime.UtcNow;
        if (inCombat)
        {
            _lastCombatUtc = now;
        }

        if (HasRecordMessage && now > _recordUntilUtc)
        {
            HasRecordMessage = false;
        }

        var state = _session.Status.Current(now);
        StatusText = state == CaptureState.Capturing && inCombat ? "En combate" : _session.Status.Describe(now);
        StatusKind = state switch
        {
            CaptureState.Failed => "error",
            CaptureState.NoTraffic => "warn",
            _ when inCombat => "live",
            _ => "idle",
        };
        EmptyText = state is CaptureState.Capturing ? "Esperando el primer golpe…" : StatusText;
        OnPropertyChanged(nameof(EffectiveOpacity));
    }

    /// <summary>
    /// Tiempo estimado para matar al objetivo al ritmo actual de la party contra él.
    /// <c>null</c> si no hay daño todavía o si tardaría más de una hora (la estimación no sirve).
    /// </summary>
    public static TimeSpan? EstimateTimeToKill(long hp, double partyDpsOnTarget)
    {
        if (hp <= 0 || partyDpsOnTarget <= 0)
        {
            return null;
        }

        var seconds = hp / partyDpsOnTarget;
        return seconds > 3600 ? null : TimeSpan.FromSeconds(Math.Ceiling(seconds));
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
        UpdateStatus(current is not null);
        var snapshot = _pinned ?? current ?? _lastFinished;
        if (snapshot is null)
        {
            return;
        }

        _shown = snapshot;
        Title = snapshot.TargetName ?? "Encuentro";
        Elapsed = snapshot.Duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        UpdateTargetHealth(snapshot.TargetHealth);
        if (IsLive && HasTargetHealth && EstimateTimeToKill(snapshot.TargetHealth!.Hp, snapshot.Actors.Sum(a => a.MainTargetDps)) is { } ttk)
        {
            TargetHealthText += $" · muere en {ttk.ToString(@"m\:ss", CultureInfo.InvariantCulture)}";
        }

        var tab = Tab;
        Func<ActorSnapshot, long> metric = a => tab.Metric(a);
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
            Actors[i].Spark = Preferences.Sparklines && Tab == MeterTab.Damage ? ActorRowViewModel.Sparkline(rows[i].Timeline) : null;
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
