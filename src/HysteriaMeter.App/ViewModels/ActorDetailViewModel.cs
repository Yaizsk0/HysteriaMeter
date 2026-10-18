using System.Collections.ObjectModel;
using System.Data.Common;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HysteriaMeter.Combat;
using HysteriaMeter.Storage;

namespace HysteriaMeter.App.ViewModels;

/// <summary>
/// Detalle de un jugador (referencia: mocks Detalle, Muertes y Comparar del rediseño): KPIs, DPS en el tiempo, pestañas
/// Skills / Muertes / Log y la vista Comparar. Se refresca con el overlay.
/// </summary>
public sealed partial class ActorDetailViewModel(SkillNames names) : ObservableObject
{
    /// <summary>De dónde sale el registro de la pelea (el motor); <c>null</c> en pruebas.</summary>
    public Func<IReadOnlyList<CombatLogEntry>>? LogSource { get; set; }

    public SkillNames Names => names;

    /// <summary>Apodo puesto a un nombre real, o <c>null</c>.</summary>
    public Func<string, string?>? NicknameOf { get; set; }

    /// <summary>Hora actual, para "HOY 21:37" en Comparar (el arnés de capturas la fija).</summary>
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;

    /// <summary>El usuario cambió el apodo (nombre real, apodo; vacío para sacarlo).</summary>
    public event Action<string, string>? NicknameChanged;

    /// <summary>Nombre del personaje en el juego, sin apodo.</summary>
    [ObservableProperty]
    public partial string RealName { get; set; } = string.Empty;

    /// <summary>Lo que se escribe en el cuadro de apodo; se guarda con <see cref="SaveNicknameCommand"/>.</summary>
    [ObservableProperty]
    public partial string Nickname { get; set; } = string.Empty;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SaveNickname()
    {
        if (RealName.Length > 0 && Nickname.Trim() != (NicknameOf?.Invoke(RealName) ?? string.Empty))
        {
            NicknameChanged?.Invoke(RealName, Nickname);
        }
    }

    /// <summary>La vista abre el registro filtrado por este jugador.</summary>
    public event Action<LogViewModel>? LogRequested;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenLog()
    {
        if (LogSource is { } source && _last is { } snapshot)
        {
            // La ventana separada conserva esta pelea al cambiar el detalle. Si el motor ya pasó a otra, no muestra
            // eventos ajenos; el origen temporal coincide con las pestañas Log y Muertes aunque falten líneas viejas.
            LogRequested?.Invoke(new LogViewModel(() => ForFight(source(), snapshot), names, ActorId, Name, BrushOf, () => snapshot.Start));
        }
    }

    // ===================== Pestañas =====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSkillsTab), nameof(IsDeathsTab), nameof(IsLogTab))]
    public partial DetailTab Tab { get; set; }

    // Mientras se compara no hay pestaña marcada: elegir una sale de la comparación.
    public bool IsSkillsTab
    {
        get => !HasComparison && Tab == DetailTab.Skills;
        set => Pick(DetailTab.Skills, value);
    }

    public bool IsDeathsTab
    {
        get => !HasComparison && Tab == DetailTab.Deaths;
        set => Pick(DetailTab.Deaths, value);
    }

    public bool IsLogTab
    {
        get => !HasComparison && Tab == DetailTab.Log;
        set => Pick(DetailTab.Log, value);
    }

    private void Pick(DetailTab tab, bool value)
    {
        if (!value)
        {
            return;
        }

        Tab = tab;
        if (HasComparison)
        {
            CompareWith = null;
        }
    }

    partial void OnTabChanged(DetailTab value) => RefreshLog(force: true);

    // ===================== Cabecera =====================

    /// <summary>Jugador elegido; <c>null</c> si no hay ninguno.</summary>
    public ulong? ActorId { get; private set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Emblema de la clase (Juego\clases); sin él, el avatar lleva una letra.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClassEmblem))]
    public partial string? ClassEmblem { get; set; }

    public bool HasClassEmblem => ClassEmblem is not null;

    /// <summary>Inicial de la clase o, si todavía no se sabe, la del nombre.</summary>
    [ObservableProperty]
    public partial string AvatarLetter { get; set; } = "?";

    // Clase con la que se armó el avatar: el emblema se busca en disco solo cuando cambia.
    private (bool Done, int? ClassId) _avatarFor;

    /// <summary>"Templario · iLvl 2130 · power 93.1k", con lo que se sepa.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial string Profile { get; set; } = string.Empty;

    public static string ProfileText(ActorSnapshot actor) =>
        string.Join(" · ", new[]
        {
            PlayerClasses.Of(actor.ClassId)?.Name,
            actor.ItemLevel is { } level ? $"iLvl {level.ToString(CultureInfo.CurrentCulture)}" : null,
            actor.CombatPower is { } power ? $"power {ActorRowViewModel.Compact(power)}" : null,
        }.Where(part => part is not null));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial string Target { get; set; } = string.Empty;

    /// <summary>Debajo del nombre: el perfil y contra quién fue la pelea.</summary>
    public string Subtitle => string.Join(" · ", new[] { Profile, Target }.Where(part => part.Length > 0));

    [ObservableProperty]
    public partial bool IsMissing { get; set; }

    [ObservableProperty]
    public partial Brush Accent { get; set; } = PlayerColors.Local;

    [ObservableProperty]
    public partial Brush BarFill { get; set; } = PlayerColors.LocalBar;

    // ===================== KPIs y línea secundaria =====================

    [ObservableProperty]
    public partial string Dps { get; set; } = "–";

    /// <summary>"+8% vs intento anterior" (solo para vos, con un intento guardado) o vacío.</summary>
    [ObservableProperty]
    public partial Delta DpsDelta { get; set; } = Delta.None;

    [ObservableProperty]
    public partial string Total { get; set; } = "–";

    /// <summary>"27,1 % del party".</summary>
    [ObservableProperty]
    public partial string ShareText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CritRate { get; set; } = "–";

    [ObservableProperty]
    public partial Delta CritDelta { get; set; } = Delta.None;

    /// <summary>Golpes por la espalda contra el objetivo principal (los modificadores solo se cuentan contra él).</summary>
    [ObservableProperty]
    public partial string BackRate { get; set; } = "–";

    [ObservableProperty]
    public partial Delta BackDelta { get; set; } = Delta.None;

    /// <summary>Percentil de tu DPS entre tus intentos guardados contra el mismo objetivo, contando esta pelea.</summary>
    [ObservableProperty]
    public partial bool HasPercentile { get; set; }

    [ObservableProperty]
    public partial string Percentile { get; set; } = "–";

    /// <summary>Escalón del color (0, 25, 50, 75, 95 o 100), el de las claves Percentile* del tema.</summary>
    [ObservableProperty]
    public partial int PercentileTier { get; set; }

    [ObservableProperty]
    public partial string PercentileHint { get; set; } = string.Empty;

    /// <summary>Tus DPS guardados contra el objetivo de la pelea, sin contarla; <c>null</c> sin historial.</summary>
    public Func<EncounterSnapshot, Task<IReadOnlyList<double>>>? PastDps { get; set; }

    private IReadOnlyList<double> _pastDps = [];

    [ObservableProperty]
    public partial string ActiveDps { get; set; } = "–";

    /// <summary>Tooltip de DPS ACTIVO: el mismo tiempo activo que ACTIVO y TIEMPO MUERTO.</summary>
    public static string ActiveDpsHint { get; } =
        $"Daño sobre el tiempo activo, el mismo de ACTIVO y TIEMPO MUERTO: no cuentan los huecos de más de {FightActivity.Tolerance} s "
        + "sin golpes directos ni el DoT que pega en ellos o después del último golpe. Si todo fue DoT, no hay tiempo activo que medir";

    [ObservableProperty]
    public partial string Duration { get; set; } = "00:00";

    [ObservableProperty]
    public partial string Overkill { get; set; } = "–";

    [ObservableProperty]
    public partial string DamageTaken { get; set; } = "–";

    [ObservableProperty]
    public partial string DeathCount { get; set; } = "0";

    /// <summary>Porcentaje del tiempo pegando, del primer golpe al último.</summary>
    [ObservableProperty]
    public partial string Uptime { get; set; } = "–";

    [ObservableProperty]
    public partial string DeadTime { get; set; } = "–";

    [ObservableProperty]
    public partial string DeadTimeHint { get; set; } = string.Empty;

    /// <summary>Consejos de la pelea (solo para el jugador local).</summary>
    public ObservableCollection<string> Advice { get; } = [];

    [ObservableProperty]
    public partial bool HasAdvice { get; set; }

    // ===================== DPS en el tiempo =====================

    /// <summary>Ventana del promedio móvil con que se calcula el pico del gráfico.</summary>
    internal const int ChartWindowSeconds = 5;

    /// <summary>Barras del gráfico, como en el mock: 40 (o una por segundo si la pelea es más corta).</summary>
    public const int ChartBarCount = 40;

    public ObservableCollection<ChartBar> ChartBars { get; } = [];

    [ObservableProperty]
    public partial int ChartColumns { get; set; } = ChartBarCount;

    [ObservableProperty]
    public partial bool HasChart { get; set; }

    /// <summary>"0:00 — 2:47".</summary>
    [ObservableProperty]
    public partial string ChartRange { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PeakDps { get; set; } = "–";

    /// <summary>" · marcas 0:42, 1:15 · boss 75% 0:50…": los momentos marcados con el atajo y las fases, o vacío.</summary>
    [ObservableProperty]
    public partial string ChartMarks { get; set; } = string.Empty;

    /// <summary>Tooltip del gráfico: el pico, las marcas y las fases del boss.</summary>
    [ObservableProperty]
    public partial string ChartTip { get; set; } = string.Empty;

    /// <summary>" · boss 75% 0:42 · 50% 1:30 · 25% 2:10": cuándo la vida del objetivo bajó de cada fase, o vacío.</summary>
    public static string PhasesText(IReadOnlyList<HealthPhase>? phases) =>
        phases is not { Count: > 0 }
            ? string.Empty
            : " · boss " + string.Join(" · ", phases.Select(p => $"{p.Percent}% {TimeSpan.FromSeconds(p.Second).ToString(@"m\:ss", CultureInfo.InvariantCulture)}"));

    public static string MarksText(IReadOnlyList<int>? marks) =>
        marks is not { Count: > 0 }
            ? string.Empty
            : " · marcas " + string.Join(", ", marks.Order().Select(m => TimeSpan.FromSeconds(m).ToString(@"m\:ss", CultureInfo.InvariantCulture)));

    // ===================== Skills =====================

    public ObservableCollection<SkillRowViewModel> Skills { get; } = [];

    /// <summary>Fila TOTAL, con las mismas columnas que las skills.</summary>
    public SkillRowViewModel Totals { get; } = new();

    // ===================== Registro integrado =====================

    /// <summary>Registro del jugador elegido para las pestañas Muertes y Log; se arma al abrir una de ellas.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLog), nameof(IsLogEmpty), nameof(LogEmptyText))]
    public partial LogViewModel? Log { get; set; }

    public bool HasLog => Log is not null;

    /// <summary>No hay líneas para mostrar (sin registro, sin golpes o nada coincide con el filtro).</summary>
    public bool IsLogEmpty => Log is not { IsEmpty: false };

    public string LogEmptyText => Log switch
    {
        null => "Sin registro de golpes para esta pelea",
        { Search.Length: > 0 } => "Nada coincide con el filtro",
        _ => "Sin golpes en el registro: solo se guarda el de la pelea en curso o la última",
    };

    /// <summary>Cada cuánto se vuelve a leer el registro del motor con la pestaña abierta (copiarlo entero cuesta).</summary>
    internal const int LogRefreshMs = 1_500;

    private long _logLoadedAt;

    // Color de cada jugador de la pelea por ID, para los nombres del registro.
    private Dictionary<ulong, Brush> _brushes = [];

    private Brush? BrushOf(ulong id) => _brushes.GetValueOrDefault(id);

    partial void OnLogChanged(LogViewModel? oldValue, LogViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.LinesChanged -= OnLogLinesChanged;
        }

        if (newValue is not null)
        {
            newValue.LinesChanged += OnLogLinesChanged;
        }

        UpdateDeathLogLine();
    }

    private void OnLogLinesChanged()
    {
        OnPropertyChanged(nameof(IsLogEmpty));
        OnPropertyChanged(nameof(LogEmptyText));
        UpdateDeathLogLine();
    }

    /// <summary>
    /// El registro del motor es el de una sola pelea (la actual o la última): si no se superpone con la que muestra el
    /// detalle (una del historial, por ejemplo), es de otra y no se muestra.
    /// </summary>
    public static IReadOnlyList<CombatLogEntry> ForFight(IReadOnlyList<CombatLogEntry> log, EncounterSnapshot snapshot) =>
        log.Count > 0 && log[0].Timestamp <= snapshot.End && log[^1].Timestamp >= snapshot.Start ? log : [];

    private IReadOnlyList<CombatLogEntry> FightLog() =>
        LogSource is { } source && _last is { } snapshot ? ForFight(source(), snapshot) : [];

    private void RefreshLog(bool force)
    {
        if (LogSource is null || ActorId is not { } id || HasComparison || Tab == DetailTab.Skills)
        {
            return;
        }

        if (Log is null || Log.FocusId != id)
        {
            Log = new LogViewModel(FightLog, names, id, Name, BrushOf, () => _last?.Start);
        }
        else if (force || Environment.TickCount64 - _logLoadedAt >= LogRefreshMs)
        {
            Log.Reload();
        }
        else
        {
            return;
        }

        _logLoadedAt = Environment.TickCount64;
    }

    // ===================== Elegir jugador y refrescar =====================

    private EncounterSnapshot? _last;

    public void Select(ActorRowViewModel row)
    {
        ActorId = row.Id;
        HasChart = false;
        ChartBars.Clear();
        Name = row.Name;
        Accent = row.Accent;
        BarFill = row.BarFill;
        AvatarLetter = row.Initial.Length > 0 ? row.Initial : "?";
        ClassEmblem = row.ClassEmblem;
        _avatarFor = default;
        Skills.Clear();
        _rebuildingOthers = true;
        CompareWith = null;
        Others.Clear();
        _rebuildingOthers = false;
        _past = [];
        _pastDps = [];
        _pastFor = null;
        HasPercentile = false;
        Deaths.Clear();
        _deathsKey = default;
        SelectedDeathIndex = -1;
        RenamingSkill = null;
        Log = null;
    }

    public void Update(EncounterSnapshot snapshot)
    {
        var actor = snapshot.Actors.FirstOrDefault(a => a.Id == ActorId);

        // Si el jugador no está en este encuentro (pelea nueva, reinicio) se deja lo último que se vio.
        IsMissing = actor is null;
        if (actor is null)
        {
            return;
        }

        _last = snapshot;
        var duration = snapshot.Duration;
        var culture = CultureInfo.CurrentCulture;
        var alias = NicknameOf?.Invoke(actor.Name);
        if (RealName != actor.Name)
        {
            RealName = actor.Name;
            Nickname = alias ?? string.Empty;
        }

        Name = alias ?? actor.Name;
        UpdateAvatar(actor);
        Profile = ProfileText(actor);
        Target = MeterViewModel.TitleOf(snapshot);
        _brushes = BrushesOf(snapshot);
        Dps = ActorRowViewModel.Compact(actor.Dps);
        ActiveDps = ActorRowViewModel.ActiveDpsText(actor.ActiveDps);
        Total = ActorRowViewModel.Compact(actor.Damage);
        ShareText = $"{actor.Share.ToString("P1", culture)} del party";
        Duration = duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        Overkill = ActorRowViewModel.Compact(actor.Overkill);
        DamageTaken = ActorRowViewModel.Compact(actor.DamageTaken);
        DeathCount = actor.Deaths.ToString(culture);
        UpdateKpis(actor);
        UpdateDeaths(snapshot, actor.Id);
        UpdateChart(actor.Timeline ?? [], snapshot.Marks);
        ChartMarks = MarksText(snapshot.Marks) + PhasesText(snapshot.Phases);
        ChartTip = $"Pico {PeakDps} DPS (promedio móvil de {ChartWindowSeconds} s){ChartMarks}"
            + (snapshot.Marks is { Count: > 0 } ? Environment.NewLine + "Barras doradas: momentos marcados con el atajo" : string.Empty);
        UpdateActivity(FightActivity.Of(actor));
        UpdateAdvice(snapshot, actor);
        if (actor.IsLocalPlayer && _pastFor != snapshot.Id)
        {
            _ = LoadPastAttemptsAsync(snapshot);
        }

        UpdateComparison(snapshot, actor);
        UpdateSkills(actor, duration);
        RefreshLog(force: false);
    }

    private void UpdateAvatar(ActorSnapshot actor)
    {
        // EmblemOf mira el disco: solo cuando cambia la clase, no en cada refresco.
        if (_avatarFor == (true, actor.ClassId))
        {
            return;
        }

        _avatarFor = (true, actor.ClassId);
        ClassEmblem = PlayerClasses.EmblemOf(actor.ClassId);
        AvatarLetter = PlayerClasses.Of(actor.ClassId)?.Name is { Length: > 0 } className
            ? char.ToUpper(className[0], CultureInfo.CurrentCulture).ToString()
            : ActorRowViewModel.InitialOf(actor.Name);
    }

    /// <summary>El color de cada jugador como en el overlay: el elegido con su acento, los demás por clase o por nombre.</summary>
    private Dictionary<ulong, Brush> BrushesOf(EncounterSnapshot snapshot)
    {
        var brushes = new Dictionary<ulong, Brush>(snapshot.Actors.Count);
        foreach (var a in snapshot.Actors)
        {
            brushes[a.Id] = a.Id == ActorId ? Accent
                : PlayerClasses.Of(a.ClassId) is { } playerClass ? PlayerColors.ForClass(playerClass.Color)
                : a.IsLocalPlayer ? PlayerColors.Local
                : PlayerColors.For(a.Name);
        }

        return brushes;
    }

    private void UpdateKpis(ActorSnapshot actor)
    {
        var culture = CultureInfo.CurrentCulture;
        var counts = actor.Counts ?? HitCounts.Empty;
        var back = counts.Rate(counts.Back);
        CritRate = actor.Hits == 0 ? "–" : actor.CritRate.ToString("P0", culture);
        BackRate = counts.Hits == 0 ? "–" : back.ToString("P0", culture);

        // Las diferencias son contra tu intento anterior: solo para vos y si hay uno guardado.
        var previous = actor.IsLocalPlayer ? _past.FirstOrDefault(o => o.Id == CompareOption.PreviousAttemptId)?.Saved : null;
        DpsDelta = previous is null ? Delta.None : PercentDelta(actor.Dps, previous.Dps);
        CritDelta = previous is null || actor.Hits == 0 || previous.Hits == 0 ? Delta.None : PointsDelta(actor.CritRate, previous.CritRate);
        var previousCounts = previous?.Counts ?? HitCounts.Empty;
        BackDelta = previous is null || counts.Hits == 0 || previousCounts.Hits == 0
            ? Delta.None
            : PointsDelta(back, previousCounts.Rate(previousCounts.Back));

        var percentile = actor.IsLocalPlayer ? PercentileAmong(actor.Dps, _pastDps) : null;
        HasPercentile = percentile is not null;
        if (percentile is { } p)
        {
            Percentile = p.Percentile.ToString(culture);
            PercentileTier = TierOf(p.Percentile);
            PercentileHint = $"entre tus {p.Attempts} intentos";
        }
    }

    /// <summary>"+8% vs intento anterior" o "−3% …"; menos del 1 % no cuenta como diferencia.</summary>
    public static Delta PercentDelta(double mine, double reference)
    {
        if (reference <= 0)
        {
            return Delta.None;
        }

        var change = (mine - reference) / reference;
        return Math.Abs(change) < 0.01
            ? new Delta("igual que el intento anterior")
            : new Delta($"{change.ToString("+0%;−0%", CultureInfo.CurrentCulture)} vs intento anterior", change > 0 ? DeltaTone.Better : DeltaTone.Worse);
    }

    /// <summary>"+3 pts" o "−5 pts" entre dos proporciones; menos de medio punto no cuenta.</summary>
    public static Delta PointsDelta(double mine, double reference)
    {
        var points = (mine - reference) * 100;
        return Math.Abs(points) < 0.5
            ? new Delta("±0 pts")
            : new Delta($"{points.ToString("+0;−0", CultureInfo.CurrentCulture)} pts", points > 0 ? DeltaTone.Better : DeltaTone.Worse);
    }

    /// <summary>Percentil de <paramref name="dps"/> entre los guardados más este; <c>null</c> sin al menos un intento guardado.</summary>
    public static (int Percentile, int Attempts)? PercentileAmong(double dps, IReadOnlyList<double> saved)
    {
        if (saved.Count == 0)
        {
            return null;
        }

        double[] all = [.. saved, dps];
        return (HistoryItem.PercentileOf(dps, all), all.Length);
    }

    /// <summary>Escalón del percentil con los umbrales de <see cref="HistoryItem.TierBrush"/>.</summary>
    public static int TierOf(int percentile) => percentile switch
    {
        >= 100 => 100,
        >= 95 => 95,
        >= 75 => 75,
        >= 50 => 50,
        >= 25 => 25,
        _ => 0,
    };

    /// <summary>
    /// Tus DPS guardados contra el objetivo de la pelea (sin contarla), del historial; vacío si no hay jugador local u
    /// objetivo, o si la base falla. <c>null</c> sin historial. El objetivo es el del historial
    /// (<see cref="FightTarget.Identity(EncounterSnapshot)"/>): una plantilla sin nombre también tiene percentil.
    /// </summary>
    public static Func<EncounterSnapshot, Task<IReadOnlyList<double>>>? PastDpsFrom(EncounterRepository? history) =>
        history is null ? null : async snapshot =>
        {
            var local = snapshot.Actors.FirstOrDefault(a => a.IsLocalPlayer);
            if (local is null || FightTarget.Identity(snapshot) is null)
            {
                return [];
            }

            try
            {
                return await history.LocalDpsAsync(snapshot.TargetName, snapshot.TargetNpcId, snapshot.Id.ToString(), local.Name).ConfigureAwait(true);
            }
            catch (DbException)
            {
                return [];
            }
        };

    private void UpdateChart(IReadOnlyList<long> timeline, IReadOnlyList<int>? marks)
    {
        HasChart = timeline.Count >= 2;
        if (!HasChart)
        {
            ChartBars.Clear();
            return;
        }

        var bars = DpsBars(timeline, marks, ChartBarCount);
        ChartColumns = bars.Count;
        Replace(ChartBars, bars);
        ChartRange = $"0:00 — {DungeonSummaryViewModel.Length(TimeSpan.FromSeconds(timeline.Count))}";
        PeakDps = ActorRowViewModel.Compact(RollingDps(timeline, ChartWindowSeconds).Max());
    }

    /// <summary>
    /// Daño por segundo agrupado en <paramref name="count"/> tramos iguales (o uno por segundo si hay menos): el alto es
    /// el DPS promedio del tramo respecto del más alto, y el tramo va dorado si tiene un momento marcado.
    /// </summary>
    public static IReadOnlyList<ChartBar> DpsBars(IReadOnlyList<long> timeline, IReadOnlyList<int>? marks, int count)
    {
        var bars = Math.Min(count, timeline.Count);
        if (bars <= 0)
        {
            return [];
        }

        var averages = new double[bars];
        var bounds = new (int From, int To)[bars];
        for (var i = 0; i < bars; i++)
        {
            var from = i * timeline.Count / bars;
            var to = (i + 1) * timeline.Count / bars;
            long sum = 0;
            for (var s = from; s < to; s++)
            {
                sum += timeline[s];
            }

            bounds[i] = (from, to);
            averages[i] = (double)sum / Math.Max(1, to - from);
        }

        var top = averages.Max();
        return
        [
            .. averages.Select((average, i) =>
            {
                var (from, to) = bounds[i];
                var marked = marks?.Any(m => m >= from && m < to) ?? false;
                var span = $"{DungeonSummaryViewModel.Length(TimeSpan.FromSeconds(from))}–{DungeonSummaryViewModel.Length(TimeSpan.FromSeconds(to))}";
                return new ChartBar(top <= 0 ? 0 : average / top, marked, $"{span} · {ActorRowViewModel.Compact(average)} DPS{(marked ? " · marcado" : string.Empty)}");
            }),
        ];
    }

    /// <summary>Cambia solo los elementos distintos (sin rearmar la lista en cada refresco).</summary>
    internal static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (i >= target.Count)
            {
                target.Add(items[i]);
            }
            else if (!EqualityComparer<T>.Default.Equals(target[i], items[i]))
            {
                target[i] = items[i];
            }
        }

        while (target.Count > items.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private void UpdateSkills(ActorSnapshot actor, TimeSpan duration)
    {
        var culture = CultureInfo.CurrentCulture;
        var skills = actor.Skills;
        var top = skills.Count > 0 ? skills[0].Damage : 0;
        for (var i = 0; i < skills.Count; i++)
        {
            if (i >= Skills.Count)
            {
                Skills.Add(new SkillRowViewModel());
            }

            Skills[i].Update(names.Of(skills[i].SkillId), skills[i], top, duration, names.IconOf(skills[i].SkillId));
        }

        while (Skills.Count > skills.Count)
        {
            Skills.RemoveAt(Skills.Count - 1);
        }

        var hits = skills.Sum(s => s.Hits);
        var counts = skills.Select(s => s.Counts ?? HitCounts.Empty).ToList();
        var countedHits = counts.Sum(c => c.Hits);
        var direct = skills.Sum(s => s.DirectDamage);
        Totals.Name = "TOTAL";
        Totals.Damage = ActorRowViewModel.Compact(actor.Damage);
        Totals.Share = actor.Damage == 0 ? "–" : 1d.ToString("P0", culture);
        Totals.Hits = hits.ToString("N0", culture);
        Totals.Critical = SkillRowViewModel.Rate(hits, skills.Sum(s => s.Crits));
        Totals.Back = SkillRowViewModel.Rate(countedHits, counts.Sum(c => c.Back));
        Totals.MaxHit = hits == 0 ? "–" : ActorRowViewModel.Compact(actor.MaxHit);
        Totals.Average = hits == 0 ? "–" : ActorRowViewModel.Compact((double)direct / hits);
        Totals.Dps = ActorRowViewModel.Compact(actor.Dps);
        Totals.Tip = SkillRowViewModel.TipText(Totals.Back, Totals.Average);
    }

    private string Display(string name) => NicknameOf?.Invoke(name) ?? name;

    private void UpdateAdvice(EncounterSnapshot snapshot, ActorSnapshot actor)
    {
        var tips = actor.IsLocalPlayer ? FightAdvice.For(snapshot, actor, Display) : [];
        HasAdvice = tips.Count > 0;
        if (!tips.SequenceEqual(Advice))
        {
            Advice.Clear();
            foreach (var tip in tips)
            {
                Advice.Add(tip);
            }
        }
    }

    private void UpdateActivity(FightActivity activity)
    {
        (Uptime, DeadTime, DeadTimeHint) = DescribeActivity(activity);
    }

    /// <summary>"92%", "12 s" y la explicación con la cantidad de huecos y el más largo.</summary>
    public static (string Uptime, string DeadTime, string Hint) DescribeActivity(FightActivity activity)
    {
        if (activity.Seconds == 0)
        {
            return ("–", "–", string.Empty);
        }

        var uptime = activity.Uptime.ToString("P0", CultureInfo.CurrentCulture);
        var dead = $"{activity.DeadSeconds} s";
        var hint = activity.Gaps == 0
            ? $"Sin huecos de más de {FightActivity.Tolerance} s sin pegar en {activity.Seconds} s de pelea"
            : $"{activity.Gaps} huecos de más de {FightActivity.Tolerance} s sin pegar (el más largo, {activity.LongestGap} s) en {activity.Seconds} s de pelea";
        return (uptime, dead, hint);
    }

    /// <summary>Promedio móvil: el DPS del segundo t es el daño de los últimos <paramref name="window"/> segundos dividido por la ventana.</summary>
    public static IReadOnlyList<double> RollingDps(IReadOnlyList<long> damageBySecond, int window)
    {
        var result = new double[damageBySecond.Count];
        long sum = 0;
        for (var i = 0; i < damageBySecond.Count; i++)
        {
            sum += damageBySecond[i];
            if (i >= window)
            {
                sum -= damageBySecond[i - window];
            }

            result[i] = (double)sum / window;
        }

        return result;
    }
}
