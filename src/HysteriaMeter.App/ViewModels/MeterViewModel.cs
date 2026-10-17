using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HysteriaMeter.App.FightCard;
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
    /// <param name="bossOnly">En la pestaña de daño, solo lo que se le hizo al objetivo principal.</param>
    public static long Metric(this MeterTab tab, ActorSnapshot actor, bool bossOnly = false) => tab switch
    {
        MeterTab.Healing => actor.Healing,
        MeterTab.Taken => actor.DamageTaken,
        _ => bossOnly ? actor.MainTargetDamage : actor.Damage,
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
    /// <param name="sessionView">Mostrar la sesión entera en vez de la pelea actual.</param>
    /// <param name="bossOnly">Contar solo el daño al objetivo principal.</param>
    public MeterViewModel(MeterSession session, MeterTab tab = MeterTab.Damage, bool showHealing = true, bool compact = false, bool sessionView = false, bool bossOnly = true, bool runView = false)
    {
        _session = session;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(session.Preferences.RefreshMs), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher.CurrentDispatcher);
        _engine = session.Engine;
        _recorder = session.Recorder;
        _history = session.History;
        Detail = new ActorDetailViewModel(session.SkillNames)
        {
            LogSource = session.Engine.CombatLog,
            NicknameOf = name => Preferences.Nicknames.GetValueOrDefault(name),
            PastAttempts = LoadPastAttemptsAsync,
            PastDps = ActorDetailViewModel.PastDpsFrom(session.History),
        };
        Detail.SkillRenamed += (skillId, name) =>
            Update(Preferences with { SkillAliases = SkillNames.With(Preferences.SkillAliases, skillId, name) }, nameof(Preferences));
        Detail.NicknameChanged += (realName, alias) =>
            Update(Preferences with { Nicknames = ViewModels.Nicknames.With(Preferences.Nicknames, realName, alias) }, nameof(Preferences));
        Tab = tab;
        _showHealing = showHealing;
        _notice = session.TakeStartupNotice();
        IsCompact = compact;
        IsSessionView = sessionView;
        IsRunView = runView && !sessionView;
        BossOnly = bossOnly;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _engine.EncounterFinished += snapshot =>
        {
            _lastFinished = snapshot;
            dispatcher.InvokeAsync(() => CheckRecordAsync(snapshot));
            if (Preferences.ImageOnKill && IsBossKill(snapshot))
            {
                dispatcher.InvokeAsync(() => SaveKillImageAsync(snapshot));
            }

            if (FightCardViewModel.CanShow(snapshot))
            {
                // La tarjeta se suscribe a la sesión: se crea solo si hay una ventana que la muestre (y la suelte).
                dispatcher.InvokeAsync(() =>
                {
                    if (FightCardRequested is { } show)
                    {
                        show(new FightCardViewModel(snapshot, _session, BossOnly && LooksLikeBossFight(snapshot)));
                    }
                });
            }
        };
        _session.PreferencesChanged += names =>
        {
            foreach (var name in names)
            {
                OnPropertyChanged(name);
            }

            _timer.Interval = TimeSpan.FromMilliseconds(Preferences.RefreshMs);
            if (names.Contains(nameof(SelectedNumberStyle)) || names.Contains(nameof(ShowSparklines)) || names.Contains(nameof(ShowRecentColumn)) || names.Contains(nameof(ShowCritColumn)) || names.Contains(nameof(ShowMaxHitColumn)) || names.Contains(nameof(ShowGear)) || names.Contains(nameof(ColorByClass)) || names.Contains(nameof(OnlyMe)) || names.Contains(nameof(MaxRows)) || names.Contains(nameof(Preferences)) || names.Contains(string.Empty))
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
        InitializeLive(dispatcher);
    }

    private MeterPreferences Preferences => _session.Preferences;

    private bool _showHealing;

    /// <summary>La ventana pide abrir otra (botón +).</summary>
    public event Action? NewWindowRequested;

    /// <summary>Se derrotó un boss: la ventana principal muestra la tarjeta de fin de pelea.</summary>
    public event Action<FightCardViewModel>? FightCardRequested;

    /// <summary>La ventana de configuración (Config/ConfigWindow) de esta ventana está abierta.</summary>
    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    /// <summary>La ventana del historial está abierta: la abre y la cierra la ventana dueña (OverlayWindow).</summary>
    [ObservableProperty]
    public partial bool IsHistoryOpen { get; set; }

    /// <summary>
    /// Encuentros guardados que pasan el filtro, del más nuevo al más viejo y agrupados por run, como los lista la
    /// ventana del historial. Se cargan al abrir el historial.
    /// </summary>
    public ObservableCollection<HistoryRun> HistoryRuns { get; } = [];

    /// <summary>"142 peleas · 31 runs": lo que deja el filtro.</summary>
    [ObservableProperty]
    public partial string HistoryCountText { get; set; } = string.Empty;

    /// <summary>Por qué la lista está vacía (no hay peleas guardadas o nada coincide); vacío si hay peleas para mostrar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistoryEmptyText))]
    public partial string HistoryEmptyText { get; set; } = string.Empty;

    public bool HasHistoryEmptyText => HistoryEmptyText.Length > 0;

    /// <summary>Al pie de la lista, si el filtro deja más peleas de las que se muestran; vacío si entran todas.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistoryMoreText))]
    public partial string HistoryMoreText { get; set; } = string.Empty;

    public bool HasHistoryMoreText => HistoryMoreText.Length > 0;

    /// <summary>Peleas que se cargan al abrir el historial (y que recorren Alt+← y Alt+→).</summary>
    public const int HistoryLimit = 200;

    private List<HistoryItem> _allHistory = [];
    private List<HistoryItem> _matchingHistory = [];
    private readonly RankTrends _rankTrends = new();

    /// <summary>Reloj del historial ("Esta semana", "hace 3 semanas"). Las pruebas lo fijan.</summary>
    public Func<DateTimeOffset> HistoryClock { get; set; } = () => DateTimeOffset.Now;

    /// <summary>Búsqueda del historial: objetivo o nota.</summary>
    [ObservableProperty]
    public partial string HistorySearch { get; set; } = string.Empty;

    /// <summary>Chip elegido: Todas, Solo kills, Solo bosses o Esta semana.</summary>
    [ObservableProperty]
    public partial HistoryFilter HistoryFilter { get; set; }

    partial void OnHistorySearchChanged(string value) => ApplyHistoryFilter();

    partial void OnHistoryFilterChanged(HistoryFilter value)
    {
        OnPropertyChanged(nameof(FilterAll));
        OnPropertyChanged(nameof(OnlyKills));
        OnPropertyChanged(nameof(FilterBosses));
        OnPropertyChanged(nameof(FilterThisWeek));
        ApplyHistoryFilter();
    }

    // Un bool por chip para los RadioButton (como ScopeMe/ScopeParty): solo el que se marca cambia el filtro.
    public bool FilterAll
    {
        get => HistoryFilter == HistoryFilter.All;
        set => SetFilter(value, HistoryFilter.All);
    }

    /// <summary>"Solo kills": los bosses derrotados (✓). Era el "Derrotados" del panel viejo.</summary>
    public bool OnlyKills
    {
        get => HistoryFilter == HistoryFilter.Kills;
        set => SetFilter(value, HistoryFilter.Kills);
    }

    public bool FilterBosses
    {
        get => HistoryFilter == HistoryFilter.Bosses;
        set => SetFilter(value, HistoryFilter.Bosses);
    }

    public bool FilterThisWeek
    {
        get => HistoryFilter == HistoryFilter.ThisWeek;
        set => SetFilter(value, HistoryFilter.ThisWeek);
    }

    private void SetFilter(bool selected, HistoryFilter filter)
    {
        if (selected)
        {
            HistoryFilter = filter;
        }
        else if (HistoryFilter == filter)
        {
            HistoryFilter = HistoryFilter.All;
        }
    }

    /// <summary>"Todos" y cada personaje con peleas guardadas; el filtro aparece con más de un personaje.</summary>
    public ObservableCollection<string> HistoryCharacters { get; } = [];

    [ObservableProperty]
    public partial bool HasSeveralCharacters { get; set; }

    [ObservableProperty]
    public partial string SelectedCharacter { get; set; } = HistoryItem.AllCharacters;

    partial void OnSelectedCharacterChanged(string value) => ApplyHistoryFilter();

    /// <summary>
    /// Pelea guardada que se está mirando (para la nota y para borrarla). En la ventana del historial es la fila marcada
    /// y la que elige el boss de "TU PROGRESO".
    /// </summary>
    [ObservableProperty]
    public partial string? PinnedHistoryId { get; private set; }

    partial void OnPinnedHistoryIdChanged(string? value) => UpdateBossProgress();

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

    public static IReadOnlyList<MeterTheme> ThemeOptions => MeterThemes.All;

    /// <summary>Tema visual; las ventanas que se abren después lo toman completo.</summary>
    public MeterTheme SelectedTheme
    {
        get => MeterThemes.Of(Preferences.Theme);
        set => Update(Preferences with { Theme = value?.Name ?? MeterThemes.Default }, nameof(SelectedTheme));
    }

    public int RowHeight
    {
        get => Preferences.RowHeight;
        set => Update(Preferences with { RowHeight = value }, nameof(RowHeight), nameof(RowNameSize), nameof(RowValueSize), nameof(RowCornerRadius), nameof(RowGap));
    }

    /// <summary>Letra del nombre acorde al alto de la fila (la del DPS es <see cref="RowValueSize"/>).</summary>
    public double RowNameSize => RowNameSizeFor(Preferences.RowHeight);

    public int MaxRows
    {
        get => Preferences.MaxRows;
        set => Update(Preferences with { MaxRows = value }, nameof(MaxRows), nameof(RowLimit), nameof(ShowAllRows));
    }

    /// <summary>
    /// Las primeras <paramref name="max"/> filas (0 = todas). Si el jugador local queda afuera, ocupa el último lugar
    /// para que siempre te veas (Details: "always show me").
    /// </summary>
    public static List<ActorSnapshot> LimitRows(List<ActorSnapshot> rows, int max)
    {
        if (max <= 0 || rows.Count <= max)
        {
            return rows;
        }

        var shown = rows.Take(max).ToList();
        if (!shown.Any(a => a.IsLocalPlayer) && rows.FirstOrDefault(a => a.IsLocalPlayer) is { } me)
        {
            shown[^1] = me;
        }

        return shown;
    }

    public int RefreshMs
    {
        get => Preferences.RefreshMs;
        set => Update(Preferences with { RefreshMs = value }, nameof(RefreshMs));
    }

    /// <summary>Item level y power al lado del nombre.</summary>
    public bool ShowGear
    {
        get => Preferences.ShowGear;
        set => Update(Preferences with { ShowGear = value }, nameof(ShowGear));
    }

    public bool ColorByClass
    {
        get => Preferences.ColorByClass;
        set => Update(Preferences with { ColorByClass = value }, nameof(ColorByClass));
    }

    public bool ShowTotalColumn
    {
        get => Preferences.ShowTotalColumn;
        set => Update(Preferences with { ShowTotalColumn = value }, nameof(ShowTotalColumn));
    }

    public bool ShowShareColumn
    {
        get => Preferences.ShowShareColumn;
        set => Update(Preferences with { ShowShareColumn = value }, nameof(ShowShareColumn));
    }

    public bool SnapWindows
    {
        get => Preferences.SnapWindows;
        set => Update(Preferences with { SnapWindows = value }, nameof(SnapWindows));
    }

    public bool LockPosition
    {
        get => Preferences.LockPosition;
        set => Update(Preferences with { LockPosition = value }, nameof(LockPosition), nameof(CanMoveWindow));
    }

    /// <summary>Se puede arrastrar y cambiar el tamaño (no está fijado).</summary>
    public bool CanMoveWindow => !Preferences.LockPosition;

    public bool ShowRecentColumn
    {
        get => Preferences.ShowRecentColumn;
        set => Update(Preferences with { ShowRecentColumn = value }, nameof(ShowRecentColumn));
    }

    public bool ShowCritColumn
    {
        get => Preferences.ShowCritColumn;
        set => Update(Preferences with { ShowCritColumn = value }, nameof(ShowCritColumn));
    }

    public bool ShowMaxHitColumn
    {
        get => Preferences.ShowMaxHitColumn;
        set => Update(Preferences with { ShowMaxHitColumn = value }, nameof(ShowMaxHitColumn));
    }

    public bool OnlyMe
    {
        get => Preferences.OnlyMe;
        set => Update(Preferences with { OnlyMe = value }, nameof(OnlyMe));
    }

    private StartupNotice? _notice;

    /// <summary>
    /// Novedades desde la última vez (se cierra con "Entendido"). La bienvenida del primer arranque ya no pasa por acá:
    /// es su propia ventana (Welcome/WelcomeWindow).
    /// </summary>
    public bool HasNotice => _notice is not null;

    public string NoticeTitle => _notice?.Title ?? string.Empty;

    public IReadOnlyList<string> NoticeLines => _notice?.Lines ?? [];

    [RelayCommand]
    private void DismissNotice()
    {
        if (_notice is { } notice)
        {
            _notice = null;
            Update(Preferences with { LastSeenNews = Math.Max(Preferences.LastSeenNews, notice.LatestId) });
            OnPropertyChanged(nameof(HasNotice));
        }
    }

    /// <summary>Congela lo que muestra el overlay (como el botón de pausa de LOA Logs); la pelea sigue contando.</summary>
    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    partial void OnIsPausedChanged(bool value)
    {
        if (!value)
        {
            Refresh();
        }
    }

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    /// <summary>Overlay minimizado a una pastilla con tu DPS (como Ember al borde de la pantalla).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFullView))]
    public partial bool IsMinimized { get; set; }

    partial void OnIsMinimizedChanged(bool value)
    {
        if (value)
        {
            IsHistoryOpen = false;
            IsSettingsOpen = false;
        }
    }

    public bool IsFullView => !IsMinimized;

    /// <summary>"12.3k · #2 de 6": tu DPS y tu puesto, o el DPS de la party si no estás en la pelea.</summary>
    [ObservableProperty]
    public partial string MinimizedText { get; set; } = "HYSTERIA";

    [RelayCommand]
    private void ToggleMinimized() => IsMinimized = !IsMinimized;

    public static string MinimizedSummary(IReadOnlyList<ActorRowViewModel> rows, string partyPerSecond, int players)
    {
        if (rows.FirstOrDefault(r => r.IsLocalPlayer) is { } me)
        {
            return $"{me.PerSecond} · #{me.Rank} de {players}";
        }

        return players == 0 ? "HYSTERIA" : $"{partyPerSecond} party";
    }

    public bool Sounds
    {
        get => Preferences.Sounds;
        set => Update(Preferences with { Sounds = value }, nameof(Sounds));
    }

    private void Alert(AlertSound sound)
    {
        if (Preferences.Sounds)
        {
            _session.PlaySound?.Invoke(sound);
        }
    }

    /// <summary>El enrage del objetivo: no se llega si al ritmo actual muere después del enrage.</summary>
    [ObservableProperty]
    public partial bool IsEnrageDanger { get; set; }

    // Pelea en la que ya se avisó que no se llega al enrage, para avisar una sola vez.
    private Guid? _enrageWarnedId;

    /// <summary>
    /// " · enrage en 1:20" (o " · ENRAGE" si ya pasó) y si no se llega: al ritmo actual el boss muere después del enrage.
    /// </summary>
    public static (string Text, bool Danger) Enrage(int enrageSeconds, TimeSpan elapsed, TimeSpan? timeToKill)
    {
        var remaining = TimeSpan.FromSeconds(enrageSeconds) - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return (" · ENRAGE", true);
        }

        var text = $" · enrage en {remaining.ToString(@"m\:ss", CultureInfo.InvariantCulture)}";
        return (text, timeToKill is { } ttk && ttk > remaining);
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

    /// <summary>Opacidad de toda la ventana del overlay (Window.Opacity), de 0,3 a 1: el slider de Configuración.</summary>
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

    /// <summary>Esta ventana muestra la sesión (todas las peleas desde el reinicio), como el "Overall" de Details!.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFightView), nameof(IsSessionChip))]
    public partial bool IsSessionView { get; set; }

    /// <summary>Esta ventana muestra la run: todas las peleas desde que entraste a la dungeon (trash y bosses juntos).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFightView), nameof(IsRunChip))]
    public partial bool IsRunView { get; set; }

    public bool IsRunChip
    {
        get => IsRunView;
        set
        {
            if (value)
            {
                IsSessionView = false;
                IsRunView = true;
            }
        }
    }

    partial void OnIsRunViewChanged(bool value) => Refresh();

    // Los chips PELEA/SESIÓN son RadioButtons: al destildarse uno escribe false, y si eso cambiara la vista
    // los dos se pisarían en un bucle infinito. Como en las pestañas, solo cuenta el que se tilda.
    public bool IsFightView
    {
        get => !IsSessionView && !IsRunView;
        set
        {
            if (value)
            {
                IsSessionView = false;
                IsRunView = false;
            }
        }
    }

    public bool IsSessionChip
    {
        get => IsSessionView;
        set
        {
            if (value)
            {
                IsRunView = false;
                IsSessionView = true;
            }
        }
    }

    partial void OnIsSessionViewChanged(bool value) => Refresh();

    /// <summary>Esta ventana cuenta solo el daño al objetivo principal (boss), sin adds.</summary>
    [ObservableProperty]
    public partial bool BossOnly { get; set; }

    partial void OnBossOnlyChanged(bool value) => Refresh();

    public int IdleSeconds
    {
        get => Preferences.IdleSeconds;
        set => Update(Preferences with { IdleSeconds = value }, nameof(IdleSeconds));
    }

    public int PracticeSeconds
    {
        get => Preferences.PracticeSeconds;
        set => Update(Preferences with { PracticeSeconds = value }, nameof(PracticeSeconds));
    }

    public string HotkeyClickThrough
    {
        get => Preferences.HotkeyClickThrough;
        set => SetHotkey(value, Preferences.HotkeyClickThrough, text => Preferences with { HotkeyClickThrough = text }, nameof(HotkeyClickThrough));
    }

    public string HotkeyReset
    {
        get => Preferences.HotkeyReset;
        set => SetHotkey(value, Preferences.HotkeyReset, text => Preferences with { HotkeyReset = text }, nameof(HotkeyReset));
    }

    public string HotkeyVisibility
    {
        get => Preferences.HotkeyVisibility;
        set => SetHotkey(value, Preferences.HotkeyVisibility, text => Preferences with { HotkeyVisibility = text }, nameof(HotkeyVisibility));
    }

    public string HotkeyMinimize
    {
        get => Preferences.HotkeyMinimize;
        set => SetHotkey(value, Preferences.HotkeyMinimize, text => Preferences with { HotkeyMinimize = text }, nameof(HotkeyMinimize));
    }

    public string HotkeyPause
    {
        get => Preferences.HotkeyPause;
        set => SetHotkey(value, Preferences.HotkeyPause, text => Preferences with { HotkeyPause = text }, nameof(HotkeyPause));
    }

    public string HotkeyMark
    {
        get => Preferences.HotkeyMark;
        set => SetHotkey(value, Preferences.HotkeyMark, text => Preferences with { HotkeyMark = text }, nameof(HotkeyMark));
    }

    /// <summary>Atajos que no se pudieron registrar (otro programa ya los usa); vacío si están todos.</summary>
    [ObservableProperty]
    public partial string HotkeyMessage { get; set; } = string.Empty;

    /// <summary>
    /// Guarda un atajo: vacío lo saca; uno que no sirve como atajo global (una letra sola) o que ya usa otra acción
    /// se ignora con un aviso.
    /// </summary>
    private void SetHotkey(string? value, string current, Func<string, MeterPreferences> with, string name)
    {
        var text = string.IsNullOrWhiteSpace(value) ? string.Empty : HotkeyGesture.Parse(value)?.ToString();
        if (text is null)
        {
            HotkeyMessage = $"\"{value}\" no sirve como atajo: usá Ctrl, Alt o Shift con otra tecla, o una tecla F";
            OnPropertyChanged(name);
            return;
        }

        if (text.Length > 0 && text != current && HotkeyTexts().Contains(text))
        {
            HotkeyMessage = $"{text} ya es otro atajo del meter";
            OnPropertyChanged(name);
            return;
        }

        if (text != current)
        {
            HotkeyMessage = string.Empty;
            Update(with(text), name);
        }
    }

    private string[] HotkeyTexts() => [Preferences.HotkeyClickThrough, Preferences.HotkeyReset, Preferences.HotkeyVisibility, Preferences.HotkeyMark, Preferences.HotkeyMinimize, Preferences.HotkeyPause, Preferences.HotkeyTab];

    /// <summary>Marca este momento de la pelea en curso; se ve en el detalle y en el JSON exportado.</summary>
    [RelayCommand]
    private void MarkMoment()
    {
        var second = _session.Engine.Mark(DateTimeOffset.Now);
        RecordMessage = second is { } s
            ? $"Momento marcado a los {TimeSpan.FromSeconds(s).ToString(@"m\:ss", CultureInfo.InvariantCulture)}"
            : "No hay pelea en curso para marcar";
        HasRecordMessage = true;
        _recordUntilUtc = DateTime.UtcNow + MarkNoticeDuration;
    }

    public static readonly TimeSpan MarkNoticeDuration = TimeSpan.FromSeconds(3);

    // Pelea cuya prueba de DPS ya terminó, para no volver a congelarla al salir del resultado.
    private Guid? _practiceDoneId;

    /// <summary>
    /// Prueba de DPS: con la pelea en vivo, cuenta hacia atrás y al llegar al tiempo la congela como resultado.
    /// Devuelve <c>true</c> si la congeló.
    /// </summary>
    private bool CheckPractice(EncounterSnapshot? current)
    {
        var limit = Preferences.PracticeSeconds;
        if (limit <= 0 || current is null || _pinned is not null || IsSessionView || IsRunView || current.Id == _practiceDoneId)
        {
            return false;
        }

        if (PracticeRemaining(current.End - current.Start, limit) is { } remaining)
        {
            StatusText = $"Prueba de DPS · quedan {remaining.ToString(@"m\:ss", CultureInfo.InvariantCulture)}";
            return false;
        }

        _practiceDoneId = current.Id;
        _pinned = current;
        PinnedHistoryId = null;
        _pinnedAt = DateTimeOffset.Now;
        CanEditNote = false;
        IsViewingHistory = true;
        HistoryLabel = $"PRUEBA DE {TimeSpan.FromSeconds(limit).ToString(@"m\:ss", CultureInfo.InvariantCulture)} · TERMINADA";
        Alert(AlertSound.Info);
        return true;
    }

    /// <summary>Lo que falta para terminar la prueba, o <c>null</c> si ya terminó.</summary>
    public static TimeSpan? PracticeRemaining(TimeSpan elapsed, int limitSeconds)
    {
        var remaining = TimeSpan.FromSeconds(limitSeconds) - elapsed;
        return remaining > TimeSpan.Zero ? TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds)) : null;
    }

    public bool AutoBackToLive
    {
        get => Preferences.AutoBackToLive;
        set => Update(Preferences with { AutoBackToLive = value }, nameof(AutoBackToLive));
    }

    /// <summary>"Iniciar con Windows": vive en el registro del usuario, no en las preferencias (es de esta PC).</summary>
    public bool StartWithWindows
    {
        get
        {
            try
            {
                return Autostart.IsEnabled();
            }
            catch (System.Security.SecurityException)
            {
                return false;
            }
        }

        set
        {
            if (Environment.ProcessPath is not { } exe)
            {
                return;
            }

            try
            {
                Autostart.Set(value, exe);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                ExportMessage = $"No se pudo cambiar el inicio con Windows: {ex.Message}";
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Datos en la carpeta del programa (portable) o en %LOCALAPPDATA%.</summary>
#pragma warning disable CA1822 // propiedad de instancia para el binding de WPF
    public string DataFolderHint => AppPaths.IsPortable
        ? $"Modo portable: datos en {AppPaths.DataFolder}"
        : $"Datos en {AppPaths.DataFolder}";
#pragma warning restore CA1822

    // Cuándo se abrió la pelea guardada: si arranca una pelea nueva después, se vuelve al vivo.
    private DateTimeOffset _pinnedAt;

    /// <summary>Modo compacto de esta ventana: sin objetivo, pestañas, encabezados ni pie.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpanded))]
    public partial bool IsCompact { get; set; }

    public bool IsExpanded => !IsCompact;

    /// <summary>
    /// Estado de la captura: "live" (en combate), "ready" (capturando, sin combate), "idle" (buscando el juego),
    /// "warn" o "error" (color del punto del encabezado).
    /// </summary>
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

    public FontFamily Font => FontOf(Preferences.TextFont);

    /// <summary>
    /// La familia de una fuente de texto elegible: las embebidas (<see cref="MeterPreferences.BundledFonts"/>) salen de
    /// Assets/Fonts y las demás del sistema. App.xaml.cs la usa para el recurso BodyFont de las ventanas.
    /// El URI nombra el ensamblado ("HysteriaMeter;component"), como los de Theme.xaml: así resuelve también cuando el
    /// ensamblado de entrada es otro (el testhost del arnés de capturas). Las dos familias Barlow se piden como "#Barlow" en carpetas separadas,
    /// el nombre con el que WPF agrupa sus pesos (ver NumberFont en Theme.xaml).
    /// </summary>
    public static FontFamily FontOf(string name)
    {
        var resource = name switch
        {
            "Barlow Condensed" => "./Assets/Fonts/BarlowCondensed/#Barlow",
            "IBM Plex Sans" => "./Assets/Fonts/IBMPlexSans/#IBM Plex Sans",
            "Barlow Semi Condensed" => "./Assets/Fonts/#Barlow",
            "Inter" => "./Assets/Fonts/#Inter",
            _ => null,
        };
        return resource is null
            ? new FontFamily(name)
            : new FontFamily(new Uri("pack://application:,,,/HysteriaMeter;component/"), resource);
    }

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

    /// <summary>Auto-verificación: cuánto de la vida que perdió el boss explica el daño contado.</summary>
    [ObservableProperty]
    public partial string? TargetHealthCheck { get; set; }

    /// <summary>Contadores de captura y decodificación, en configuración.</summary>
    [ObservableProperty]
    public partial string Diagnostics { get; set; } = string.Empty;

    /// <summary>
    /// Auto-verificación (MT-11): el daño que contamos contra el boss comparado con la vida que perdió desde el spawn.
    /// Con el filtro de party no incluye a jugadores de afuera, así que puede dar menos de 100 % sin ser un error.
    /// </summary>
    public static string? HealthCheck(Health? health, long countedDamage)
    {
        if (health is not { MaxHp: > 0 } || health.MaxHp - health.Hp is var lost && lost <= 0)
        {
            return null;
        }

        var ratio = (double)countedDamage / lost;
        return $"El daño contado explica el {ratio.ToString("P0", CultureInfo.CurrentCulture)} de la vida que perdió ({ActorRowViewModel.Compact(lost)}). " +
            "Menos de 100 %: pegaron jugadores fuera del filtro o el boss ya estaba herido; más: curación del boss.";
    }

    /// <summary>
    /// Aviso de parche (RB-01): mucho tráfico del juego pero ningún paquete conocido, o muchos errores al leer los
    /// conocidos. Suele pasar cuando una actualización del juego cambia los opcodes o el formato.
    /// </summary>
    public static bool PatchSuspected(long frames, long unknown, long errors)
    {
        var known = frames - unknown;
        return (frames >= 3_000 && known == 0) || (errors >= 50 && errors > known / 5);
    }

    public static string DescribeDecoder(long frames, long unknown, long errors, long resyncs)
    {
        var culture = CultureInfo.CurrentCulture;
        return $"Frames {frames.ToString("N0", culture)} · conocidos {(frames - unknown).ToString("N0", culture)} · " +
            $"errores {errors.ToString("N0", culture)} · resincronizaciones {resyncs.ToString("N0", culture)}";
    }

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

    partial void OnIsClickThroughChanged(bool value)
    {
        if (value)
        {
            IsHistoryOpen = false;
        }
    }

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

    private bool _started;

    public void Start()
    {
        if (!_started && _history is not null)
        {
            _history.Saved += OnHistorySaved;
        }

        _started = true;
        _timer.Start();
    }

    public void Stop()
    {
        _started = false;
        _timer.Stop();
        if (_history is not null)
        {
            _history.Saved -= OnHistorySaved;
        }
    }

    private void OnHistorySaved() => _timer.Dispatcher.InvokeAsync(async () =>
    {
        if (_started && IsHistoryOpen)
        {
            await ReloadHistoryAsync().ConfigureAwait(true);
        }
    });

    partial void OnTabChanged(MeterTab value) => Refresh();

    [RelayCommand]
    private static void OpenRecordings()
    {
        Directory.CreateDirectory(CaptureRecorder.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{CaptureRecorder.Folder}\"") { UseShellExecute = true });
    }

    /// <summary>Copia el reporte (versión, sistema, diagnóstico; sin nombres) y abre un issue nuevo en GitHub.</summary>
    [RelayCommand]
    private void ReportProblem()
    {
        var report = BugReport.Build(Version, Environment.OSVersion.VersionString, StatusText, Diagnostics, Preferences);
        try
        {
            System.Windows.Clipboard.SetText(report);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Si el portapapeles está ocupado, igual va en la URL.
        }

        Process.Start(new ProcessStartInfo(BugReport.IssueUrl(report)) { UseShellExecute = true });
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
    public static string ExportFolder { get; } = AppPaths.In("Exportes");

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

    /// <summary>La vista guarda la imagen del overlay en esa ruta (.png).</summary>
    public event Action<string>? SaveImageRequested;

    public bool ImageOnKill
    {
        get => Preferences.ImageOnKill;
        set => Update(Preferences with { ImageOnKill = value }, nameof(ImageOnKill));
    }

    /// <summary>Espera a que el overlay muestre la pelea terminada antes de sacar la imagen.</summary>
    private static readonly TimeSpan KillImageDelay = TimeSpan.FromSeconds(1.5);

    private async Task SaveKillImageAsync(EncounterSnapshot snapshot)
    {
        await Task.Delay(KillImageDelay).ConfigureAwait(true);

        // La imagen usa las filas del overlay: historial y configuración son ventanas independientes.
        if (_shown?.Id == snapshot.Id && IsFullView)
        {
            SaveImageRequested?.Invoke(KillImagePath(snapshot));
        }
    }

    /// <summary>Peleas de al menos este largo cuentan como boss aunque no estén marcadas en data/npcs.json.</summary>
    public static readonly TimeSpan BossKillMinDuration = BossRules.BossKillMinDuration;

    /// <summary>Parte del daño que tiene que ir al objetivo principal para tomar la pelea como de boss.</summary>
    public const double BossFightShare = BossRules.BossFightShare;

    /// <summary>"Solo boss" solo tiene sentido contra un boss; en un pull de trash se cuenta todo.</summary>
    public static bool LooksLikeBossFight(EncounterSnapshot snapshot) => BossRules.LooksLikeBossFight(snapshot);

    public static bool IsBossKill(EncounterSnapshot snapshot) => BossRules.IsBossKill(snapshot);

    /// <summary>Exportes\2026-10-16 21-03 Tarka.png, con el mismo nombre que el CSV y el JSON.</summary>
    public static string KillImagePath(EncounterSnapshot snapshot) =>
        Path.Combine(ExportFolder, $"{EncounterExport.FileName(snapshot, snapshot.Start.ToLocalTime())}.png");

    [RelayCommand]
    private void ExportCsv() => Export("csv", EncounterExport.ToCsv);

    [RelayCommand]
    private void ExportJson() => Export("json", EncounterExport.ToJson);

    /// <summary>Ruta del perfil de configuración que se exporta y se importa.</summary>
    public static string ProfilePath => Path.Combine(ExportFolder, ProfileFile.FileName);

    [RelayCommand]
    private void ExportProfile()
    {
        try
        {
            Directory.CreateDirectory(ExportFolder);
            File.WriteAllText(ProfilePath, ProfileFile.ToJson(Preferences));
            ExportMessage = $"Perfil guardado en Exportes\\{ProfileFile.FileName} (sin el webhook)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ExportMessage = $"No se pudo guardar el perfil: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ImportProfile()
    {
        string json;
        try
        {
            json = File.ReadAllText(ProfilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ExportMessage = $"Poné el perfil en Exportes\\{ProfileFile.FileName} para cargarlo";
            return;
        }

        if (ProfileFile.FromJson(json, Preferences) is not { } imported)
        {
            ExportMessage = $"{ProfileFile.FileName} no es un perfil válido";
            return;
        }

        // Cadena vacía: todas las propiedades cambiaron.
        Update(imported, string.Empty);
        ExportMessage = "Perfil cargado";
    }

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

    /// <summary>Pide copiar las filas del overlay; las ventanas auxiliares quedan abiertas.</summary>
    [RelayCommand]
    private void CopyImage()
    {
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

    /// <summary>Abre o cierra la ventana de configuración (la abre el overlay al ver el cambio de IsSettingsOpen).</summary>
    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    [RelayCommand]
    private async Task ToggleHistoryAsync()
    {
        IsHistoryOpen = !IsHistoryOpen;
        if (!IsHistoryOpen)
        {
            return;
        }

        await ReloadHistoryAsync().ConfigureAwait(true);
    }

    private int _historyReload;

    private async Task ReloadHistoryAsync()
    {
        var reload = ++_historyReload;
        if (_history is not null)
        {
            try
            {
                var saved = (await _history.ListRecentAsync(HistoryLimit).ConfigureAwait(true)).Select(HistoryItem.From).ToList();
                if (reload != _historyReload)
                {
                    return;
                }

                _allHistory = [.. HistoryItem.WithDeltas(HistoryItem.WithCharacterTags(HistoryItem.WithComparisons(saved)))];
            }
            catch (DbException)
            {
                // La base todavía no está lista (la inicializa el pipeline al arrancar): el historial queda vacío.
                _allHistory = [];
            }
        }

        UpdateHistoryCharacters();
        ApplyHistoryFilter();
    }

    private void UpdateHistoryCharacters()
    {
        var characters = _allHistory.Select(i => i.Character).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.CurrentCulture).ToList();
        HasSeveralCharacters = characters.Count > 1;
        var selected = SelectedCharacter;
        HistoryCharacters.Clear();
        HistoryCharacters.Add(HistoryItem.AllCharacters);
        foreach (var character in characters)
        {
            HistoryCharacters.Add(character);
        }

        SelectedCharacter = HistoryCharacters.Contains(selected) ? selected : HistoryItem.AllCharacters;
    }

    /// <summary>Columna "TU PROGRESO": el boss de la fila marcada, o el que deja el filtro si deja uno solo.</summary>
    [ObservableProperty]
    public partial BossProgress Progress { get; set; } = BossProgress.Empty(BossProgress.PickText);

    [ObservableProperty]
    public partial bool HasProgress { get; set; }

    /// <summary>"Tarka · 8 intentos · último 11.2k · mejor 12.4k · kill más rápida 2:25".</summary>
    public static string ProgressSummary(string target, IReadOnlyList<double> dps, TimeSpan? fastestKill = null) =>
        $"{target} · {dps.Count} intentos · último {ActorRowViewModel.Compact(dps[^1])} · mejor {ActorRowViewModel.Compact(dps.Max())}"
        + (fastestKill is { } kill ? $" · kill más rápida {kill.ToString(@"m\:ss", CultureInfo.InvariantCulture)}" : string.Empty);

    /// <summary>Peleas visibles en la lista; el conteo, las runs y el progreso usan todas las que coinciden.</summary>
    private const int HistoryVisible = 60;

    private void ApplyHistoryFilter()
    {
        var now = HistoryClock();
        _matchingHistory = [.. _allHistory.Where(i => i.Matches(HistorySearch, HistoryFilter, SelectedCharacter, now))];
        var visible = _matchingHistory.Take(HistoryVisible).ToList();

        HistoryRuns.Clear();
        foreach (var run in HistoryRun.Group(visible, _allHistory))
        {
            HistoryRuns.Add(run);
        }

        HistoryCountText = HistoryRun.CountText(_matchingHistory);
        HistoryMoreText = _matchingHistory.Count > visible.Count
            ? $"Se muestran las {visible.Count} peleas más nuevas de {_matchingHistory.Count}: buscá o filtrá para ver las anteriores."
            : string.Empty;
        HistoryEmptyText = _allHistory.Count == 0
            ? "Todavía no hay peleas guardadas: cada pelea aparece acá cuando termina."
            : _matchingHistory.Count == 0 ? "Ninguna pelea coincide con la búsqueda o el filtro." : string.Empty;
        UpdateBossProgress();
    }

    private void UpdateBossProgress()
    {
        var selected = PinnedHistoryId is { } id ? _allHistory.Find(i => i.Id == id) : null;
        Progress = BossProgress.For(_matchingHistory, selected, HistoryClock(), _allHistory);
        HasProgress = Progress.HasData;
    }

    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (PinnedHistoryId is not { } id || _history is null)
        {
            return;
        }

        var note = string.IsNullOrWhiteSpace(CurrentNote) ? null : CurrentNote.Trim();
        try
        {
            await _history.SetNoteAsync(id, note).ConfigureAwait(true);
            var index = _allHistory.FindIndex(i => i.Id == id);
            if (index >= 0)
            {
                _allHistory[index] = _allHistory[index] with { Note = note };
            }
        }
        catch (DbException)
        {
            return;
        }

        // Con la ventana del historial abierta, la nota nueva se ve en su fila.
        if (IsHistoryOpen)
        {
            ApplyHistoryFilter();
        }
    }

    /// <summary>Borra la pelea guardada que se está mirando y vuelve al vivo.</summary>
    [RelayCommand]
    private async Task DeleteEncounterAsync()
    {
        if (PinnedHistoryId is not { } id || _history is null)
        {
            return;
        }

        try
        {
            await _history.DeleteAsync(id).ConfigureAwait(true);
            await ReloadHistoryAsync().ConfigureAwait(true);
        }
        catch (DbException)
        {
            return;
        }

        BackToLive();
        if (IsHistoryOpen)
        {
            UpdateHistoryCharacters();
            ApplyHistoryFilter();
        }
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
        double? previous;
        try
        {
            best = await _history.BestLocalDpsAsync(target, snapshot.Id.ToString(), local.Name).ConfigureAwait(true);
            previous = await _history.PreviousLocalDpsAsync(target, snapshot.Id.ToString(), local.Name).ConfigureAwait(true);
        }
        catch (DbException)
        {
            return;
        }

        if ((RecordText(target, local.Dps, best) ?? VsPreviousText(local.Dps, previous)) is not { } message)
        {
            return;
        }

        Alert(AlertSound.Info);
        // Si se abre la tarjeta de fin de pelea, ella ya dice "vs tu intento anterior" y el récord: el aviso no lo repite.
        if (FinishedNotice(message, cardOpens: FightCardViewModel.CanShow(snapshot) && FightCardRequested is not null) is { } shown)
        {
            RecordMessage = shown;
            HasRecordMessage = true;
            _recordUntilUtc = DateTime.UtcNow + RecordNoticeDuration;
        }
    }

    /// <summary>El aviso de récord o de comparación del overlay; ninguno cuando la tarjeta de fin de pelea lo muestra.</summary>
    public static string? FinishedNotice(string message, bool cardOpens) => cardOpens ? null : message;

    /// <summary>
    /// El nombre del objetivo; si el juego no lo mandó y no está en data/npcs.json, "NPC 12345" para poder agregarlo.
    /// </summary>
    public static string TitleOf(EncounterSnapshot snapshot) =>
        snapshot.TargetName ?? (snapshot.TargetNpcId is { } npc ? $"NPC {npc.ToString(CultureInfo.InvariantCulture)}" : "Encuentro");

    /// <summary>"¡Récord en Tarka! 12.4k DPS (antes 11.0k)", o <c>null</c> si no lo superó o es la primera vez.</summary>
    public static string? RecordText(string target, double dps, double? previousBest) =>
        previousBest is { } best && dps > best
            ? $"¡Récord en {target}! {ActorRowViewModel.Compact(dps)} DPS (antes {ActorRowViewModel.Compact(best)})"
            : null;

    /// <summary>Tu intento anterior y tu mejor intento contra el objetivo, para "Comparar con" del detalle.</summary>
    private async Task<IReadOnlyList<CompareOption>> LoadPastAttemptsAsync(EncounterSnapshot snapshot)
    {
        if (_history is null || snapshot.TargetName is not { Length: > 0 } target)
        {
            return [];
        }

        try
        {
            var localName = snapshot.Actors.FirstOrDefault(a => a.IsLocalPlayer)?.Name;
            var (previousId, bestId) = await _history.PastAttemptsAsync(target, snapshot.Id.ToString(), localName).ConfigureAwait(true);
            var previous = previousId is null ? null : await _history.LoadAsync(previousId).ConfigureAwait(true);
            var best = bestId is null || bestId == previousId ? null : await _history.LoadAsync(bestId).ConfigureAwait(true);
            return
            [
                .. new[]
                {
                    CompareOption.Attempt(CompareOption.PreviousAttemptId, bestId == previousId ? "Tu intento anterior (tu mejor)" : "Tu intento anterior", previous),
                    CompareOption.Attempt(CompareOption.BestAttemptId, "Tu mejor intento", best),
                }.OfType<CompareOption>(),
            ];
        }
        catch (DbException)
        {
            return [];
        }
    }

    /// <summary>"+8% vs tu intento anterior (11.0k DPS)", o <c>null</c> si no hay intento anterior o la diferencia es mínima.</summary>
    public static string? VsPreviousText(double dps, double? previous)
    {
        if (previous is not { } before || before <= 0)
        {
            return null;
        }

        var change = (dps - before) / before;
        if (Math.Abs(change) < 0.01)
        {
            return null;
        }

        var percent = (change * 100).ToString("+0;-0", CultureInfo.InvariantCulture);
        return $"{percent}% vs tu intento anterior ({ActorRowViewModel.Compact(before)} DPS)";
    }

    /// <summary>Alt+← o ‹: la pelea guardada anterior, sin abrir el historial (como los segmentos de Details!).</summary>
    [RelayCommand]
    private Task OlderFightAsync() => StepFightAsync(+1);

    /// <summary>Alt+→ o ›: la pelea siguiente; desde la más nueva vuelve al vivo.</summary>
    [RelayCommand]
    private Task NewerFightAsync() => StepFightAsync(-1);

    private async Task StepFightAsync(int step)
    {
        if (_history is null || (PinnedHistoryId is null && step < 0))
        {
            return;
        }

        List<HistoryItem> items;
        try
        {
            items = [.. (await _history.ListRecentAsync(HistoryLimit).ConfigureAwait(true)).Select(HistoryItem.From)];
        }
        catch (DbException)
        {
            return;
        }

        // La lista va de la más nueva a la más vieja; desde el vivo (o una pelea abierta de archivo) la anterior es la primera.
        var index = PinnedHistoryId is null ? -1 : items.FindIndex(i => i.Id == PinnedHistoryId);
        var next = index + step;
        if (next < 0)
        {
            await SaveNoteAsync().ConfigureAwait(true);
            BackToLive();
        }
        else if (next < items.Count)
        {
            await OpenEncounterAsync(items[next]).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task OpenEncounterAsync(HistoryItem? item)
    {
        if (item is null || _history is null)
        {
            return;
        }

        if (CanEditNote && PinnedHistoryId is not null)
        {
            await SaveNoteAsync().ConfigureAwait(true);
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
        _pinnedAt = DateTimeOffset.Now;
        PinnedHistoryId = item.Id;
        CurrentNote = (_allHistory.Find(i => i.Id == item.Id) ?? item).Note ?? string.Empty;
        CanEditNote = true;
        IsViewingHistory = true;
        HistoryLabel = $"HISTORIAL · {item.When}";

        // Las ventanas auxiliares quedan abiertas; el overlay muestra la pelea y el historial marca su fila.
        Refresh();
    }

    /// <summary>Pide abrir el modo dungeon con el resumen de una run.</summary>
    public event Action<DungeonSummaryViewModel>? RunSummaryRequested;

    /// <summary>Modo dungeon: carga todas las peleas de la run de esa fila (misma entrada a la zona) y arma el resumen.</summary>
    [RelayCommand]
    private async Task OpenRunSummaryAsync(HistoryItem? item)
    {
        if (item?.Run is not { } run || _history is null)
        {
            return;
        }

        var fights = new List<EncounterSnapshot>();
        try
        {
            foreach (var fight in _allHistory.Where(i => i.Run == run))
            {
                if (await _history.LoadAsync(fight.Id).ConfigureAwait(true) is { } snapshot)
                {
                    fights.Add(snapshot);
                }
            }
        }
        catch (DbException)
        {
            return;
        }

        var entered = DateTimeOffset.TryParse(run, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : (DateTimeOffset?)null;
        RunSummaryRequested?.Invoke(new DungeonSummaryViewModel(DungeonSummary.From(fights, entered)));
    }

    /// <summary>Muestra una pelea inventada para acomodar el overlay sin combatir.</summary>
    [RelayCommand]
    private void ShowDemo()
    {
        _pinned = DemoEncounter.Create(DateTimeOffset.Now);
        PinnedHistoryId = null;
        CanEditNote = false;
        IsViewingHistory = true;
        HistoryLabel = "DEMO · datos de prueba";
        Refresh();
    }

    /// <summary>
    /// Abre una pelea guardada con "Guardar JSON" (propia o que te pasó otro de la legión) y la muestra fija en el overlay.
    /// Devuelve <c>false</c> si el archivo no se pudo leer o no es una pelea.
    /// </summary>
    public bool OpenSharedFile(string path)
    {
        EncounterSnapshot? snapshot;
        try
        {
            snapshot = EncounterExport.FromJson(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ExportMessage = $"No se pudo abrir: {ex.Message}";
            return false;
        }

        if (snapshot is null)
        {
            ExportMessage = $"{Path.GetFileName(path)} no es una pelea exportada por Hysteria Meter";
            return false;
        }

        _pinned = snapshot;
        PinnedHistoryId = null;
        CanEditNote = false;
        IsViewingHistory = true;
        HistoryLabel = $"ARCHIVO · {Path.GetFileNameWithoutExtension(path)}";
        ExportMessage = string.Empty;
        Refresh();
        return true;
    }

    // Pelea fijada desde la tarjeta de fin de pelea: como una del historial, vuelve al vivo sola con la pelea siguiente
    // (AutoBackToLive). Las otras formas de fijar ponen otro snapshot en _pinned, así que la comparación deja de dar.
    private Guid? _cardFightId;

    /// <summary>
    /// La tarjeta de fin de pelea actúa sobre su pelea, no sobre lo que muestre el overlay: si muestra otra cosa (vista
    /// RUN o SESIÓN, pausa, otra pelea fijada), la fija como al abrirla del historial. Se sale con Esc, con "Volver al
    /// vivo" o sola cuando empieza otra pelea.
    /// </summary>
    public void ShowFight(EncounterSnapshot fight)
    {
        // La pausa puede dejar una instantánea anterior con el mismo ID: solo la misma instancia ya está al día.
        if (ReferenceEquals(_shown, fight))
        {
            return;
        }

        _pinned = fight;
        PinnedHistoryId = null;
        _pinnedAt = DateTimeOffset.Now;
        _cardFightId = fight.Id;
        CanEditNote = false;
        IsViewingHistory = true;
        HistoryLabel = $"FIN DE PELEA · {TitleOf(fight)}";
        Refresh();
    }

    /// <summary>
    /// "Ver detalle" de la tarjeta: tu fila en esa pelea (aunque la pestaña de la ventana no te muestre, por ejemplo CURA
    /// sin curación). <c>false</c> si no estás en la pelea.
    /// </summary>
    public bool ShowFightDetail(EncounterSnapshot fight)
    {
        if (!fight.Actors.Any(a => a.IsLocalPlayer))
        {
            return false;
        }

        ShowFight(fight);
        Detail.Select(Actors.FirstOrDefault(a => a.IsLocalPlayer) ?? LocalRow(fight));
        // En pausa Refresh no toca el detalle: se carga acá con la pelea de la tarjeta.
        Detail.Update(fight);
        DetailRequested?.Invoke();
        return true;
    }

    /// <summary>
    /// "Guardar captura" de la tarjeta: la misma imagen que la automática al derrotar un boss (esa pelea, con el overlay
    /// entero). Si el overlay muestra otra cosa, primero fija la pelea (<see cref="ShowFight"/>) y ahí
    /// queda. La pastilla, la franja de pelea fijada y el estilo de pausa se esconden solo mientras <paramref name="render"/>
    /// dibuja; devuelve lo que devuelve <paramref name="render"/> (el mensaje de la tarjeta).
    /// </summary>
    public string SaveFightImage(EncounterSnapshot fight, Func<string> render)
    {
        ShowFight(fight);
        var view = (IsMinimized, IsViewingHistory, IsPaused);
        (IsMinimized, IsViewingHistory, IsPaused) = (false, false, false);
        try
        {
            return render();
        }
        finally
        {
            (IsMinimized, IsViewingHistory, IsPaused) = view;
        }
    }

    /// <summary>Tu fila armada a mano, para el detalle, cuando la pestaña de la ventana no la muestra.</summary>
    private ActorRowViewModel LocalRow(EncounterSnapshot fight)
    {
        var me = ViewModels.Nicknames.Apply(fight, Preferences.Nicknames).Actors.First(a => a.IsLocalPlayer);
        var row = new ActorRowViewModel();
        row.Update(1, me, MeterTab.Damage, me.Damage, me.Damage, fight.Duration, showGear: Preferences.ShowGear, colorByClass: Preferences.ColorByClass);
        return row;
    }

    [RelayCommand]
    private void ToggleCompact() => IsCompact = !IsCompact;

    /// <summary>
    /// Esc, de lo que está más arriba a lo de abajo: cierra el panel abierto (es otra ventana, encima del overlay); si no
    /// hay, cierra las novedades como "Entendido"; si tampoco, deja la pelea vieja y vuelve a la actual.
    /// </summary>
    [RelayCommand]
    private void Escape()
    {
        if (IsSettingsOpen || IsHistoryOpen)
        {
            IsSettingsOpen = false;
            IsHistoryOpen = false;
            return;
        }

        if (HasNotice)
        {
            DismissNotice();
            return;
        }

        if (IsViewingHistory)
        {
            BackToLive();
        }
    }

    [RelayCommand]
    private void BackToLive()
    {
        _pinned = null;
        PinnedHistoryId = null;
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
        PinnedHistoryId = null;
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
            CaptureState.Capturing => "ready",
            _ => "idle",
        };
        var decoder = _session.Decoder;
        var (frames, unknown, errors) = (decoder.TotalFrames, decoder.UnknownFrames, decoder.Errors);
        if (IsSettingsOpen)
        {
            Diagnostics = DescribeDecoder(frames, unknown, errors, decoder.Resyncs);
        }

        var patch = state == CaptureState.Capturing && PatchSuspected(frames, unknown, errors);
        if (patch)
        {
            StatusText = "El juego pudo haberse actualizado: no se reconocen los paquetes. Revisá si hay una versión nueva del meter.";
            StatusKind = "warn";
        }

        EmptyText = StatusKind is not ("warn" or "error") ? "Esperando el primer golpe…" : StatusText;
        OnPropertyChanged(nameof(EffectiveOpacity));
        UpdateLiveStatus(state, now, patch);
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
        TargetHealthText = $"{ActorRowViewModel.Compact(health.Hp)} / {ActorRowViewModel.Compact(health.MaxHp)}";
    }

    private void Refresh()
    {
        var current = _engine.CurrentSnapshot();
        var followsLive = PinnedHistoryId is not null || (_pinned is { } pinned && pinned.Id == _cardFightId);
        if (followsLive && Preferences.AutoBackToLive && current is not null && current.Start.ToLocalTime() > _pinnedAt)
        {
            BackToLive();
            return;
        }

        UpdateStatus(current is not null);
        CheckPractice(current);
        IsLive = _pinned is null && current is not null;
        TrackLive(current);
        if (_pinned is null && IsRunView)
        {
            current = _engine.RunSnapshot();
        }
        else if (_pinned is null && IsSessionView)
        {
            current = _engine.SessionSnapshot();
        }

        if (IsPaused && _pinned is null && _shown is not null)
        {
            return;
        }

        var snapshot = _pinned ?? current ?? _lastFinished;
        if (snapshot is null)
        {
            ShowNothing();
            return;
        }

        var real = snapshot;
        snapshot = ViewModels.Nicknames.Apply(snapshot, Preferences.Nicknames);
        _shown = snapshot;
        Title = TitleOf(snapshot);
        Elapsed = snapshot.Duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        UpdateTargetHealth(snapshot.TargetHealth);
        TargetHealthCheck = HealthCheck(snapshot.TargetHealth, snapshot.Actors.Sum(a => a.MainTargetDamage));
        var ttk = IsLive && HasTargetHealth ? EstimateTimeToKill(snapshot.TargetHealth!.Hp, snapshot.Actors.Sum(a => a.MainTargetDps)) : null;
        if (ttk is { } kill)
        {
            TargetHealthText += $" · muere en {kill.ToString(@"m\:ss", CultureInfo.InvariantCulture)}";
        }

        UpdateEnrage(real, ttk);

        var tab = Tab;
        var bossOnly = BossOnly && LooksLikeBossFight(snapshot);
        Func<ActorSnapshot, long> metric = a => tab.Metric(a, bossOnly);
        var combined = ShowHealingColumn;
        var rows = snapshot.Actors
            .Where(a => metric(a) > 0 || (combined && a.Healing > 0))
            .OrderByDescending(metric)
            .ThenByDescending(a => a.Healing)
            .ToList();
        var total = rows.Sum(metric);
        var top = rows.Count > 0 ? metric(rows[0]) : 0;
        var ranked = rows;
        _rankTrends.Begin(snapshot.Id, ((int)tab * 2) + (bossOnly ? 1 : 0));
        var now = DateTimeOffset.Now;
        if (Preferences.OnlyMe && rows.Any(a => a.IsLocalPlayer))
        {
            rows = [.. rows.Where(a => a.IsLocalPlayer)];
        }

        rows = LimitRows(rows, Preferences.MaxRows);

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

            var rank = ranked.IndexOf(rows[i]) + 1;
            Actors[i].Update(rank, rows[i], Tab, top, total, snapshot.Duration, bossOnly, Preferences.ShowGear, Preferences.ColorByClass);
            Actors[i].Trend = _rankTrends.Update(rows[i].Id, rank, now);
            Actors[i].Spark = Preferences.Sparklines && Tab == MeterTab.Damage ? ActorRowViewModel.Sparkline(rows[i].Timeline) : null;
            Actors[i].Recent = Preferences.ShowRecentColumn && Tab == MeterTab.Damage
                ? ActorRowViewModel.Compact(ActorRowViewModel.RecentDps(rows[i].Timeline, snapshot.Duration))
                : "–";
            Actors[i].Crit = Tab == MeterTab.Damage && rows[i].Hits > 0 ? rows[i].CritRate.ToString("P0", CultureInfo.CurrentCulture) : "–";
            Actors[i].MaxHitText = Tab == MeterTab.Damage && rows[i].MaxHit > 0 ? ActorRowViewModel.Compact(rows[i].MaxHit) : "–";
            Actors[i].HealingPerSecond = combined && rows[i].Healing > 0
                ? ActorRowViewModel.Compact(rows[i].Healing / snapshot.Duration.TotalSeconds)
                : "–";
            Actors[i].RecentTip = Tab == MeterTab.Damage
                ? ActorRowViewModel.Compact(ActorRowViewModel.RecentDps(rows[i].Timeline, snapshot.Duration))
                : string.Empty;
            Actors[i].TopSkill = Tab == MeterTab.Damage ? ActorRowViewModel.TopSkillOf(rows[i], _session.SkillNames) : string.Empty;
        }

        while (Actors.Count > rows.Count)
        {
            Actors.RemoveAt(Actors.Count - 1);
        }

        IsEmpty = Actors.Count == 0;
        MinimizedText = MinimizedSummary(Actors, PartyPerSecond, ranked.Count);
        UpdateLive(real, ranked.Count);
        if (Detail.ActorId is not null)
        {
            Detail.Update(real);
        }
    }
}
