using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HysteriaMeter.App.Config;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Problema de captura que el overlay muestra como tarjeta en el área de filas.</summary>
public enum CaptureIssue
{
    None,

    /// <summary>La captura se cortó y Npcap no está instalado (o el error lo nombra).</summary>
    Npcap,

    /// <summary>Se captura, pero no llega tráfico del juego (juego cerrado, otra placa de red, VPN).</summary>
    NoTraffic,

    /// <summary>La captura se cortó por otro motivo.</summary>
    Failed,

    /// <summary>Llega tráfico pero no se reconocen los paquetes: el juego pudo haberse actualizado.</summary>
    Patch,
}

/// <summary>Cómo se pinta un chip de la vista RUN.</summary>
public enum RunChipKind
{
    Trash,
    Kill,
    Wipe,

    /// <summary>El boss que se está peleando ahora.</summary>
    Current,
}

/// <summary>Un chip de la vista RUN: "Trash 11:20", "Kromede ✓", "Tarka ✕2" o "Tarka · en curso".</summary>
public sealed record RunChip(string Text, RunChipKind Kind);

/// <summary>Una pelea de la run para los chips: cuándo fue, contra qué y cómo terminó (con las reglas de BossRules).</summary>
public sealed record RunFight(DateTimeOffset Start, DateTimeOffset End, string Title, RunSegmentKind Kind, DateTimeOffset? Zone, bool InProgress = false)
{
    public static RunFight From(EncounterSnapshot snapshot, bool inProgress = false) => new(
        snapshot.Start,
        snapshot.End,
        MeterViewModel.TitleOf(snapshot),
        !BossRules.IsBossAttempt(snapshot) ? RunSegmentKind.Trash : snapshot.TargetKilled ? RunSegmentKind.Kill : RunSegmentKind.Wipe,
        snapshot.ZoneEnteredAt,
        inProgress);
}

/// <summary>
/// Lo que suma el overlay en vivo del rediseño: estado de la captura y sus tarjetas, espera entre peleas, bloque del
/// objetivo ("Intento N", enrage), vista RUN, avisos superpuestos, pie y pastilla. Todo sale del motor, de la captura
/// o de la base: lo que no tiene fuente no se muestra.
/// </summary>
public sealed partial class MeterViewModel
{
    /// <summary>Cuánto queda visible el aviso de boss detectado.</summary>
    public static readonly TimeSpan BossNoticeDuration = TimeSpan.FromSeconds(6);

    /// <summary>Cuánto queda visible el aviso de captura guardada.</summary>
    public static readonly TimeSpan SavedNoticeDuration = TimeSpan.FromSeconds(8);

    /// <summary>Cada cuánto se reintenta leer la última pelea guardada si la base todavía no estaba lista.</summary>
    private static readonly TimeSpan LastFightRetry = TimeSpan.FromSeconds(2);

    // La pelea en curso tal como la da el motor (antes de reemplazarla por la run o la sesión).
    private EncounterSnapshot? _liveCurrent;

    private bool? _npcapInstalled;
    private bool _lastFightLoaded;
    private DateTime _lastFightRetryUtc;
    private Task _lastFightQuery = Task.CompletedTask;

    private (Guid Id, uint? NpcId)? _bossNoticeKey;
    private DateTime _bossNoticeUntilUtc;
    private DateTime _savedNoticeUntilUtc;

    private (Guid Id, string Target, string? Local)? _attemptKey;
    private int _attempt;
    private bool _attemptVisible;
    private Task _attemptQuery = Task.CompletedTask;

    /// <summary>Consultas a la base del overlay en vivo que pueden estar en curso (la última pelea, el intento).</summary>
    public Task LiveQueries => Task.WhenAll(_lastFightQuery, _attemptQuery);

    private void InitializeLive(Dispatcher dispatcher)
    {
        _engine.EncounterFinished += snapshot =>
        {
            dispatcher.InvokeAsync(() => LastFightText = LastFightLine(TitleOf(snapshot), snapshot.Duration));
        };
        _session.ResetRequested += ClearLive;
        _session.PreferencesChanged += _ =>
        {
            OnPropertyChanged(nameof(ShowColumnHeaders));
            OnPropertyChanged(nameof(RowValueSize));
            OnPropertyChanged(nameof(PauseHint));
        };
        PropertyChanged += OnLivePropertyChanged;
    }

    private void OnLivePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IsPaused):
            case nameof(IsViewingHistory):
                OnPropertyChanged(nameof(IsPauseBannerVisible));
                break;
            case nameof(IsCompact):
                OnPropertyChanged(nameof(ShowColumnHeaders));
                OnPropertyChanged(nameof(ShowFooter));
                break;
            case nameof(IsEmpty):
                OnPropertyChanged(nameof(ShowEmptyTabText));
                break;
            case nameof(Tab):
                OnPropertyChanged(nameof(EmptyTabText));
                break;
            case nameof(HasNotice):
                OnPropertyChanged(nameof(NoticeKicker));
                OnPropertyChanged(nameof(NoticeHeading));
                OnPropertyChanged(nameof(HasNoticeHeading));
                break;
        }
    }

    /// <summary>Reiniciar: el overlay vuelve a la tarjeta de espera (con la última pelea) y la run arranca de cero.</summary>
    private void ClearLive()
    {
        _liveCurrent = null;
        RunChips.Clear();
        ShowRunBlock = false;
        HasFight = false;
        HasBossNotice = false;
        IsEnrageDanger = false;
        HasEnrage = false;
        AttemptText = string.Empty;
        _attemptKey = null;
        PillRow = null;
    }

    /// <summary>Hay una pelea en pantalla (en vivo, terminada o abierta del historial); sin ella va la tarjeta de espera.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWaitingCard), nameof(ShowNpcapCard), nameof(ShowTrafficCard), nameof(ShowFailedCard), nameof(ShowClock), nameof(ShowEmptyTabText), nameof(ShowFooter))]
    public partial bool HasFight { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWaitingCard), nameof(ShowNpcapCard), nameof(ShowTrafficCard), nameof(ShowFailedCard), nameof(ShowPatchNotice))]
    public partial CaptureIssue Issue { get; set; }

    public bool ShowWaitingCard => !HasFight && Issue is CaptureIssue.None or CaptureIssue.Patch;

    public bool ShowNpcapCard => !HasFight && Issue == CaptureIssue.Npcap;

    public bool ShowTrafficCard => !HasFight && Issue == CaptureIssue.NoTraffic;

    public bool ShowFailedCard => !HasFight && Issue == CaptureIssue.Failed;

    /// <summary>El aviso de parche se ve también con una pelea en pantalla: los números pueden estar incompletos.</summary>
    public bool ShowPatchNotice => Issue == CaptureIssue.Patch;

    /// <summary>El reloj de la pelea; sin pelea, en su lugar va la etiqueta del estado de la captura.</summary>
    public bool ShowClock => HasFight;

    /// <summary>"Capturando", "Sin tráfico", "Sin Npcap"...: lo corto del encabezado; el texto largo va en el tooltip.</summary>
    [ObservableProperty]
    public partial string StatusLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WaitingTitle { get; set; } = WaitingReady;

    private const string WaitingReady = "Listo para la próxima pelea";

    /// <summary>"Última: Tarka · 3:12", o vacío si todavía no hubo ninguna.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WaitingDetail))]
    public partial string LastFightText { get; set; } = string.Empty;

    public string WaitingDetail => LastFightText.Length > 0 ? LastFightText : "Esperando el primer golpe…";

    /// <summary>El mensaje de la excepción que cortó la captura, para la tarjeta de error.</summary>
    [ObservableProperty]
    public partial string FailedDetail { get; set; } = string.Empty;

    /// <summary>Con una pelea en pantalla pero sin filas en esta pestaña (nadie curó, nadie recibió daño).</summary>
    public bool ShowEmptyTabText => HasFight && IsEmpty;

    public string EmptyTabText => Tab switch
    {
        MeterTab.Healing => "Nadie curó todavía",
        MeterTab.Taken => "Nadie recibió daño todavía",
        _ => "Todavía no hay daño",
    };

    /// <summary>"Última: Tarka · 3:12".</summary>
    public static string LastFightLine(string title, TimeSpan duration) => $"Última: {title} · {Clock(duration)}";

    /// <summary>"3:12" o, desde una hora, "1:03:12".</summary>
    public static string Clock(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Qué tarjeta de captura corresponde. Sin Npcap la excepción no siempre lo nombra (puede ser un DLL que no carga),
    /// así que también cuenta que no esté instalado.
    /// </summary>
    public static CaptureIssue IssueOf(CaptureState state, string? failure, bool patch, bool npcapInstalled) => state switch
    {
        CaptureState.Failed when !npcapInstalled || (failure?.Contains("Npcap", StringComparison.OrdinalIgnoreCase) ?? false) => CaptureIssue.Npcap,
        CaptureState.Failed => CaptureIssue.Failed,
        CaptureState.NoTraffic => CaptureIssue.NoTraffic,
        CaptureState.Capturing when patch => CaptureIssue.Patch,
        _ => CaptureIssue.None,
    };

    /// <summary>La etiqueta corta del encabezado para cada estado de la captura.</summary>
    public static string StatusLabelOf(CaptureState state, CaptureIssue issue) => issue switch
    {
        CaptureIssue.Npcap => "Sin Npcap",
        CaptureIssue.Failed => "Captura detenida",
        CaptureIssue.NoTraffic => "Sin tráfico",
        CaptureIssue.Patch => "¿Juego actualizado?",
        _ => state == CaptureState.Starting ? "Buscando el juego…" : "Capturando",
    };

    /// <summary>Npcap deja wpcap.dll en System32\Npcap (o en System32 con el modo compatible con WinPcap).</summary>
    private static bool NpcapInstalled()
    {
        var system = Environment.SystemDirectory;
        return File.Exists(Path.Combine(system, "Npcap", "wpcap.dll")) || File.Exists(Path.Combine(system, "wpcap.dll"));
    }

    /// <summary>Lo del estado de la captura que agrega el rediseño; lo llama UpdateStatus en cada refresco.</summary>
    private void UpdateLiveStatus(CaptureState state, DateTime now, bool patch)
    {
        var failure = _session.Status.Failure;
        if (state == CaptureState.Failed)
        {
            _npcapInstalled ??= NpcapInstalled();
        }

        Issue = IssueOf(state, failure, patch, _npcapInstalled ?? true);
        StatusLabel = StatusLabelOf(state, Issue);
        WaitingTitle = state == CaptureState.Starting ? StatusText : WaitingReady;
        FailedDetail = failure ?? string.Empty;
        if (HasBossNotice && now > _bossNoticeUntilUtc)
        {
            HasBossNotice = false;
        }

        if (HasSavedNotice && now > _savedNoticeUntilUtc)
        {
            HasSavedNotice = false;
        }

        if (!_lastFightLoaded && LastFightText.Length == 0 && _history is not null && _lastFightQuery.IsCompleted && now >= _lastFightRetryUtc)
        {
            _lastFightQuery = LoadLastFightAsync();
        }
    }

    /// <summary>
    /// "Última: …" al abrir el meter, con la última pelea guardada. La base la inicializa el pipeline en segundo plano:
    /// si todavía no está lista se reintenta en el próximo refresco.
    /// </summary>
    private async Task LoadLastFightAsync()
    {
        try
        {
            var recent = await _history!.ListRecentAsync(1).ConfigureAwait(true);
            _lastFightLoaded = true;
            if (LastFightText.Length == 0 && recent is [var last]
                && DateTimeOffset.TryParse(last.StartedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start)
                && DateTimeOffset.TryParse(last.EndedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end))
            {
                LastFightText = LastFightLine(last.TargetName ?? "Encuentro", end - start);
            }
        }
        catch (DbException)
        {
            _lastFightRetryUtc = DateTime.UtcNow + LastFightRetry;
        }
    }

    // ===================== Objetivo =====================

    /// <summary>"26,3 %": la vida que le queda al objetivo, con un decimal.</summary>
    [ObservableProperty]
    public partial string TargetHealthPercent { get; set; } = string.Empty;

    /// <summary>Vida absoluta, "muere en m:ss" y la auto-verificación del daño, en el tooltip del porcentaje.</summary>
    [ObservableProperty]
    public partial string TargetHealthTip { get; set; } = string.Empty;

    /// <summary>"Party 48.2k/s".</summary>
    [ObservableProperty]
    public partial string PartyRateText { get; set; } = string.Empty;

    /// <summary>"Enrage en 1:20" (o "ENRAGE" si ya pasó), si data/npcs.json trae el enrage del objetivo.</summary>
    [ObservableProperty]
    public partial string EnrageText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasEnrage { get; set; }

    /// <summary>"ENRAGE EN 0:30" en la alerta roja (mientras no se llegue al enrage al ritmo actual).</summary>
    [ObservableProperty]
    public partial string EnrageAlertTitle { get; set; } = string.Empty;

    /// <summary>"Kromede · 22 % de vida · al ritmo actual muere en 1:05".</summary>
    [ObservableProperty]
    public partial string EnrageAlertDetail { get; set; } = string.Empty;

    /// <summary>Lo que falta para el enrage, o <c>null</c> si ya pasó.</summary>
    public static TimeSpan? EnrageRemaining(int enrageSeconds, TimeSpan elapsed)
    {
        var remaining = TimeSpan.FromSeconds(enrageSeconds) - elapsed;
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    private void UpdateEnrage(EncounterSnapshot snapshot, TimeSpan? timeToKill)
    {
        if (!IsLive || !HasTargetHealth || snapshot.TargetEnrageSeconds is not { } enrage)
        {
            IsEnrageDanger = false;
            HasEnrage = false;
            return;
        }

        var elapsed = snapshot.MainTargetDuration ?? snapshot.Duration;
        var (_, danger) = Enrage(enrage, elapsed, timeToKill);
        var remaining = EnrageRemaining(enrage, elapsed);
        EnrageText = remaining is { } left ? $"Enrage en {left.ToString(@"m\:ss", CultureInfo.InvariantCulture)}" : "ENRAGE";
        HasEnrage = true;
        EnrageAlertTitle = remaining is { } r ? $"ENRAGE EN {r.ToString(@"m\:ss", CultureInfo.InvariantCulture)}" : "¡ENRAGE!";
        EnrageAlertDetail = $"{TitleOf(snapshot)} · {TargetHealthRatio.ToString("P0", CultureInfo.CurrentCulture)} de vida"
            + (timeToKill is { } kill ? $" · al ritmo actual muere en {kill.ToString(@"m\:ss", CultureInfo.InvariantCulture)}" : string.Empty);
        IsEnrageDanger = danger;

        // La alerta queda mientras dure el peligro; el sonido, una vez por pelea.
        if (danger && _enrageWarnedId != snapshot.Id)
        {
            _enrageWarnedId = snapshot.Id;
            Alert(AlertSound.Warning);
        }
    }

    /// <summary>"Intento 3": solo contra bosses (BossRules) y desde el segundo intento.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAttempt))]
    public partial string AttemptText { get; set; } = string.Empty;

    public bool HasAttempt => AttemptText.Length > 0;

    /// <summary>
    /// El intento contra el objetivo: las peleas guardadas antes contra el mismo objetivo y con el mismo personaje, más
    /// esta. En vivo (pelea, no run ni sesión) y en una pelea del historial; la demo, un archivo y la prueba de DPS no
    /// lo muestran. La cuenta se pide una vez por pelea y objetivo.
    /// </summary>
    private void UpdateAttempt(EncounterSnapshot real)
    {
        var eligible = _history is not null
            && real.TargetName is { Length: > 0 }
            && BossRules.IsBossAttempt(real)
            && (_pinned is null ? !IsRunView && !IsSessionView : PinnedHistoryId is not null);
        _attemptVisible = eligible;
        if (!eligible)
        {
            AttemptText = string.Empty;
            return;
        }

        var key = (real.Id, real.TargetName!, real.Actors.FirstOrDefault(a => a.IsLocalPlayer)?.Name);
        if (key != _attemptKey)
        {
            _attemptKey = key;
            _attempt = 0;
            _attemptQuery = LoadAttemptAsync(key, real.Start);
        }

        AttemptText = AttemptLine(_attempt);
    }

    /// <summary>"Intento 3", o vacío en el primero (o si no se sabe).</summary>
    public static string AttemptLine(int attempt) => attempt >= 2 ? $"Intento {attempt.ToString(CultureInfo.CurrentCulture)}" : string.Empty;

    private async Task LoadAttemptAsync((Guid Id, string Target, string? Local) key, DateTimeOffset start)
    {
        int earlier;
        try
        {
            earlier = await _history!.CountEarlierAttemptsAsync(key.Target, start, key.Id.ToString(), key.Local).ConfigureAwait(true);
        }
        catch (DbException)
        {
            return;
        }

        if (_attemptKey == key)
        {
            _attempt = earlier + 1;
            if (_attemptVisible)
            {
                AttemptText = AttemptLine(_attempt);
            }
        }
    }

    // ===================== Vista RUN =====================

    /// <summary>La vista RUN en vivo: título de la run, tiempo desde la entrada y chips por tramo en vez del objetivo.</summary>
    [ObservableProperty]
    public partial bool ShowRunBlock { get; set; }

    /// <summary>"Run · entrada 21:04" (no hay nombre de zona), o "Run · desde 21:04" si no se vio la entrada.</summary>
    [ObservableProperty]
    public partial string RunHeader { get; set; } = string.Empty;

    /// <summary>Tiempo desde la entrada hasta el último golpe, como el título de la run del motor.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRunElapsed))]
    public partial string RunElapsed { get; set; } = string.Empty;

    public bool HasRunElapsed => RunElapsed.Length > 0;

    public ObservableCollection<RunChip> RunChips { get; } = [];

    /// <summary>
    /// Chips de la run en orden: el trash consecutivo va junto ("Trash 11:20", el lapso de esas peleas), cada kill es
    /// "Boss ✓", los wipes seguidos contra el mismo boss se juntan ("Tarka ✕2") y el boss en curso es "Tarka · en curso".
    /// </summary>
    public static List<RunChip> BuildRunChips(IReadOnlyList<RunFight> fights)
    {
        var chips = new List<RunChip>();
        var i = 0;
        while (i < fights.Count)
        {
            var fight = fights[i];
            if (fight.Kind == RunSegmentKind.Trash)
            {
                var last = i;
                while (last + 1 < fights.Count && fights[last + 1].Kind == RunSegmentKind.Trash)
                {
                    last++;
                }

                chips.Add(new RunChip($"Trash {Clock(fights[last].End - fight.Start)}", RunChipKind.Trash));
                i = last + 1;
            }
            else if (fight.Kind == RunSegmentKind.Kill)
            {
                // Un boss muerto es kill aunque la pelea siga abierta hasta el corte por inactividad.
                chips.Add(new RunChip($"{fight.Title} ✓", RunChipKind.Kill));
                i++;
            }
            else if (fight.InProgress)
            {
                chips.Add(new RunChip($"{fight.Title} · en curso", RunChipKind.Current));
                i++;
            }
            else
            {
                var count = 1;
                while (i + count < fights.Count && fights[i + count] is { Kind: RunSegmentKind.Wipe, InProgress: false } next && next.Title == fight.Title)
                {
                    count++;
                }

                chips.Add(new RunChip(count == 1 ? $"{fight.Title} ✕" : $"{fight.Title} ✕{count.ToString(CultureInfo.InvariantCulture)}", RunChipKind.Wipe));
                i += count;
            }
        }

        return chips;
    }

    private void UpdateRun(EncounterSnapshot real)
    {
        ShowRunBlock = IsRunView && _pinned is null;
        if (!ShowRunBlock)
        {
            return;
        }

        // Las peleas de esta run (la misma entrada a la zona que la run del motor) y la que está en curso.
        var zone = real.ZoneEnteredAt;
        var fights = _session.RunFights(zone);
        if (_liveCurrent is { } current && current.ZoneEnteredAt == zone)
        {
            fights.Add(RunFight.From(current, inProgress: true));
        }

        RunHeader = zone is { } entered
            ? $"Run · entrada {entered.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}"
            : $"Run · desde {real.Start.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}";
        RunElapsed = fights.Count == 0 ? string.Empty : Clock(fights.Max(f => f.End) - (zone ?? real.Start));

        var chips = BuildRunChips(fights);
        if (!chips.SequenceEqual(RunChips))
        {
            RunChips.Clear();
            foreach (var chip in chips)
            {
                RunChips.Add(chip);
            }
        }
    }

    // ===================== Avisos =====================

    /// <summary>Los avisos superpuestos van solo en la ventana principal (lo decide la vista al abrirse).</summary>
    [ObservableProperty]
    public partial bool ShowsToasts { get; set; }

    [ObservableProperty]
    public partial bool HasBossNotice { get; set; }

    [ObservableProperty]
    public partial string BossNoticeName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BossNoticeDetail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSavedNotice { get; set; }

    /// <summary>"Captura guardada en Exportes"; la ruta completa va en el tooltip.</summary>
    [ObservableProperty]
    public partial string SavedNoticeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SavedNoticePath { get; set; } = string.Empty;

    /// <summary>
    /// Lo que se mira de la pelea en curso en cada refresco, antes de cambiarla por la run o la sesión y aunque el
    /// overlay esté en pausa: el aviso de boss sale al empezar la pelea (o cuando el objetivo pasa a ser un boss).
    /// </summary>
    private void TrackLive(EncounterSnapshot? current)
    {
        _liveCurrent = current;
        if (_pinned is not null || current is not { TargetIsBoss: true } || (current.Id, current.TargetNpcId) == _bossNoticeKey)
        {
            return;
        }

        _bossNoticeKey = (current.Id, current.TargetNpcId);
        BossNoticeName = TitleOf(current);
        BossNoticeDetail = current.TargetHealth is { MaxHp: > 0 } health
            ? $"{ActorRowViewModel.Compact(health.MaxHp)} de vida · marcado como boss en la tabla de NPCs"
            : "Marcado como boss en la tabla de NPCs";
        HasBossNotice = true;
        _bossNoticeUntilUtc = DateTime.UtcNow + BossNoticeDuration;
    }

    /// <summary>La vista guardó la imagen del overlay: aviso con el link a la carpeta.</summary>
    public void ShowSavedImage(string path)
    {
        SavedNoticePath = path;
        SavedNoticeText = $"Captura guardada en {Path.GetFileName(Path.GetDirectoryName(path))}";
        HasSavedNotice = true;
        _savedNoticeUntilUtc = DateTime.UtcNow + SavedNoticeDuration;
    }

    // ===================== Filas, pie y pastilla =====================

    /// <summary>Letra del DPS de cada fila según el alto (Barlow, un punto más que el nombre).</summary>
    public double RowValueSize => RowValueSizeFor(Preferences.RowHeight);

    /// <summary>Letra del nombre: 13 en filas de hasta 24 px, 18 desde 36 y 16 en el medio (la del mock).</summary>
    public static double RowNameSizeFor(int rowHeight) => rowHeight switch { <= 24 => 13, >= 36 => 18, _ => 16 };

    public static double RowValueSizeFor(int rowHeight) => rowHeight switch { <= 24 => 14, >= 36 => 19, _ => 17 };

    /// <summary>El encabezado de columnas sobra con solo DPS y %: aparece cuando hay alguna columna opcional.</summary>
    public bool ShowColumnHeaders => IsExpanded && (ShowTotalColumn || ShowRecentColumn || ShowCritColumn || ShowMaxHitColumn);

    /// <summary>El pie (total, jugadores) va con una pelea en pantalla y fuera del modo compacto.</summary>
    public bool ShowFooter => IsExpanded && HasFight;

    /// <summary>"6 jugadores".</summary>
    [ObservableProperty]
    public partial string PlayersText { get; set; } = string.Empty;

    public static string PlayersLine(int players) => players == 1 ? "1 jugador" : $"{players.ToString(CultureInfo.CurrentCulture)} jugadores";

    /// <summary>Tooltip del total: el ritmo de la party y el crítico y el golpe máximo tuyos.</summary>
    [ObservableProperty]
    public partial string FooterTip { get; set; } = string.Empty;

    /// <summary>Tu fila, para la pastilla minimizada (insignia y DPS); <c>null</c> si no estás en la pelea.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPillRow))]
    public partial ActorRowViewModel? PillRow { get; set; }

    public bool HasPillRow => PillRow is not null;

    /// <summary>"#2 de 6".</summary>
    [ObservableProperty]
    public partial string PillRankText { get; set; } = string.Empty;

    /// <summary>Franja de pausa: congela lo que se ve del vivo (con una pelea guardada abierta no hace nada).</summary>
    public bool IsPauseBannerVisible => IsPaused && !IsViewingHistory;

    /// <summary>
    /// "Ctrl+Shift+P para seguir", en el pie mientras está en pausa. Sin atajo, señala el botón de la franja, que dibuja
    /// las dos barras de pausa (como el mock).
    /// </summary>
    public string PauseHint => Preferences.HotkeyPause.Length > 0 ? $"{Preferences.HotkeyPause} para seguir" : PauseHintWithoutHotkey;

    public const string PauseHintWithoutHotkey = "Tocá ‖ en la franja de arriba para seguir";

    /// <summary>Lo del rediseño que sale de la pelea en pantalla; lo llama Refresh después de armar las filas.</summary>
    private void UpdateLive(EncounterSnapshot real, int players)
    {
        HasFight = true;
        TargetHealthPercent = HasTargetHealth ? TargetHealthRatio.ToString("P1", CultureInfo.CurrentCulture) : string.Empty;
        TargetHealthTip = TargetHealthCheck is { Length: > 0 } check ? $"{TargetHealthText}{Environment.NewLine}{check}" : TargetHealthText;
        PartyRateText = $"Party {PartyPerSecond}/s";
        PlayersText = PlayersLine(players);
        FooterTip = $"Party {PartyPerSecond} {RateUnit} · Crít. {CritRate} · Máx. {MaxHit}";
        PillRow = Actors.FirstOrDefault(a => a.IsLocalPlayer);
        PillRankText = PillRow is { } me ? $"#{me.Rank.ToString(CultureInfo.CurrentCulture)} de {players.ToString(CultureInfo.CurrentCulture)}" : string.Empty;
        UpdateAttempt(real);
        UpdateRun(real);
    }

    /// <summary>
    /// Volver al vivo sin nada que mostrar (se dejó la demo o se borró la pelea abierta antes de pelear): las filas de
    /// antes ya no corresponden y vuelve la tarjeta de espera.
    /// </summary>
    private void ShowNothing()
    {
        if (!HasFight)
        {
            return;
        }

        _shown = null;
        Actors.Clear();
        Title = NoTarget;
        Elapsed = "00:00";
        HasTargetHealth = false;
        IsEnrageDanger = false;
        HasEnrage = false;
        AttemptText = string.Empty;
        ShowRunBlock = false;
        PillRow = null;
        PartyPerSecond = "0";
        PartyTotal = "0";
        CritRate = "–";
        MaxHit = "–";
        MinimizedText = MinimizedSummary(Actors, PartyPerSecond, 0);
        IsEmpty = true;
        HasFight = false;
    }

    // ===================== Novedades =====================

    /// <summary>"NOVEDADES · v0.68.0" (o el título del aviso, si es otro).</summary>
    public string NoticeKicker => IsNewsNotice ? $"NOVEDADES · {ShortVersion(Version)}".TrimEnd(' ', '·') : NoticeTitle;

    /// <summary>"Qué cambió" en las novedades; los otros avisos no llevan título grande.</summary>
    public string NoticeHeading => IsNewsNotice ? "Qué cambió" : string.Empty;

    public bool HasNoticeHeading => NoticeHeading.Length > 0;

    // NewsFeed.NoticeFor titula así a las novedades.
    private bool IsNewsNotice => NoticeTitle == "NOVEDADES";

    /// <summary>"v0.68.0" de "v0.68.0 · a352982"; vacío si la versión no se conoce.</summary>
    public static string ShortVersion(string version)
    {
        var cut = version.IndexOf(" · ", StringComparison.Ordinal);
        var head = cut < 0 ? version : version[..cut];
        return head.StartsWith('v') ? head : string.Empty;
    }

    // ===================== Acciones de las tarjetas =====================

    /// <summary>"Elegir placa de red": abre Captura o cambia a esa sección si la configuración ya está abierta.</summary>
    [RelayCommand]
    private void OpenCaptureSettings() => OpenSettings(ConfigSection.Captura);

    [RelayCommand]
    private static void OpenNpcapDownload() =>
        Process.Start(new ProcessStartInfo("https://npcap.com/#download") { UseShellExecute = true });
}
