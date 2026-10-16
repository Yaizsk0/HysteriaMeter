using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Una muerte del jugador elegido, para la sección MUERTES del detalle.</summary>
public sealed record DeathItem(string When, string Summary, string LastHits);

/// <summary>Otro jugador de la pelea, para comparar.</summary>
public sealed record CompareOption(ulong Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Una fila de la comparación: qué se mide, el valor de cada uno y si el elegido gana.</summary>
public sealed record CompareRow(string Label, string Mine, string Theirs, bool MineBetter, bool TheirsBetter);

/// <summary>Desglose por skill de un jugador (referencia: mocks/Detalle.dc.html). Se refresca con el overlay.</summary>
public sealed partial class ActorDetailViewModel(SkillNames names) : ObservableObject
{
    /// <summary>De dónde sale el registro de la pelea (el motor); <c>null</c> en pruebas.</summary>
    public Func<IReadOnlyList<CombatLogEntry>>? LogSource { get; set; }

    public SkillNames Names => names;

    /// <summary>Apodo puesto a un nombre real, o <c>null</c>.</summary>
    public Func<string, string?>? NicknameOf { get; set; }

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

    /// <summary>Skill que se está renombrando (doble clic en su fila); <c>null</c> si ninguna.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenamingSkill), nameof(RenamingSkillLabel))]
    public partial SkillRowViewModel? RenamingSkill { get; set; }

    public bool IsRenamingSkill => RenamingSkill is not null;

    public string RenamingSkillLabel => RenamingSkill is { } skill ? $"Nombre para la skill {skill.SkillId}:" : string.Empty;

    [ObservableProperty]
    public partial string SkillNameDraft { get; set; } = string.Empty;

    /// <summary>Se guardó un nombre de skill (vacío lo saca); lo persiste quien guarda las preferencias.</summary>
    public event Action<uint, string>? SkillRenamed;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void StartRenameSkill(SkillRowViewModel? skill)
    {
        RenamingSkill = skill;
        SkillNameDraft = skill is null || skill.Name.StartsWith("Skill ", StringComparison.Ordinal) ? string.Empty : skill.Name;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SaveSkillName()
    {
        if (RenamingSkill is { } skill)
        {
            SkillRenamed?.Invoke(skill.SkillId, SkillNameDraft);
            skill.Name = names.Of(skill.SkillId);
        }

        RenamingSkill = null;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void CancelSkillRename() => RenamingSkill = null;

    /// <summary>La vista abre el registro filtrado por este jugador.</summary>
    public event Action<LogViewModel>? LogRequested;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenLog()
    {
        if (LogSource is { } source)
        {
            LogRequested?.Invoke(new LogViewModel(source, names, ActorId, Name));
        }
    }

    [ObservableProperty]
    public partial string DamageTaken { get; set; } = "–";

    [ObservableProperty]
    public partial string DeathCount { get; set; } = "0";

    public ObservableCollection<DeathItem> Deaths { get; } = [];

    /// <summary>Consejos de la pelea (solo para el jugador local).</summary>
    public ObservableCollection<string> Advice { get; } = [];

    [ObservableProperty]
    public partial bool HasAdvice { get; set; }

    /// <summary>Los demás jugadores de la pelea, para el selector "Comparar con".</summary>
    public ObservableCollection<CompareOption> Others { get; } = [];

    [ObservableProperty]
    public partial CompareOption? CompareWith { get; set; }

    public ObservableCollection<CompareRow> Comparison { get; } = [];

    [ObservableProperty]
    public partial bool HasComparison { get; set; }

    private EncounterSnapshot? _last;

    // Mientras se rearma la lista de jugadores, el ComboBox escribe null en CompareWith: no hay que reaccionar.
    private bool _rebuildingOthers;

    partial void OnCompareWithChanged(CompareOption? value)
    {
        if (!_rebuildingOthers && _last is { } snapshot)
        {
            Update(snapshot);
        }
    }

    /// <summary>Porcentaje del tiempo pegando, del primer golpe al último.</summary>
    [ObservableProperty]
    public partial string Uptime { get; set; } = "–";

    [ObservableProperty]
    public partial string DeadTime { get; set; } = "–";

    [ObservableProperty]
    public partial string DeadTimeHint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasDeaths { get; set; }

    /// <summary>Jugador elegido; <c>null</c> si no hay ninguno.</summary>
    public ulong? ActorId { get; private set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>"Templario · iLvl 2130 · power 93.1k", con lo que se sepa.</summary>
    [ObservableProperty]
    public partial string Profile { get; set; } = string.Empty;

    public static string ProfileText(ActorSnapshot actor) =>
        string.Join(" · ", new[]
        {
            PlayerClasses.Of(actor.ClassId)?.Name,
            actor.ItemLevel is { } level ? $"iLvl {level.ToString(CultureInfo.CurrentCulture)}" : null,
            actor.CombatPower is { } power ? $"power {ActorRowViewModel.Compact(power)}" : null,
        }.Where(part => part is not null));

    [ObservableProperty]
    public partial string Target { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Brush Accent { get; set; } = PlayerColors.Local;

    [ObservableProperty]
    public partial Brush BarFill { get; set; } = PlayerColors.LocalBar;

    [ObservableProperty]
    public partial string Dps { get; set; } = "–";

    [ObservableProperty]
    public partial string ActiveDps { get; set; } = "–";

    [ObservableProperty]
    public partial string Total { get; set; } = "–";

    [ObservableProperty]
    public partial string Duration { get; set; } = "00:00";

    [ObservableProperty]
    public partial string Overkill { get; set; } = "–";

    [ObservableProperty]
    public partial bool IsMissing { get; set; }

    /// <summary>Ventana del promedio móvil del gráfico, como en el mock ("ventana 5 s").</summary>
    internal const int ChartWindowSeconds = 5;

    /// <summary>
    /// DPS en el tiempo: x = segundo, y = pico − DPS (WPF crece hacia abajo). El primer punto es (0, pico)
    /// para que la base del gráfico sea DPS 0; el Polyline se estira al tamaño del recuadro.
    /// </summary>
    [ObservableProperty]
    public partial PointCollection ChartPoints { get; set; } = [];

    [ObservableProperty]
    public partial bool HasChart { get; set; }

    [ObservableProperty]
    public partial string PeakDps { get; set; } = "–";

    /// <summary>" · marcas 0:42, 1:15" con los momentos marcados con el atajo, o vacío.</summary>
    [ObservableProperty]
    public partial string ChartMarks { get; set; } = string.Empty;

    public static string MarksText(IReadOnlyList<int>? marks) =>
        marks is not { Count: > 0 }
            ? string.Empty
            : " · marcas " + string.Join(", ", marks.Order().Select(m => TimeSpan.FromSeconds(m).ToString(@"m\:ss", CultureInfo.InvariantCulture)));

    public ObservableCollection<SkillRowViewModel> Skills { get; } = [];

    /// <summary>Fila TOTAL, con las mismas columnas que las skills.</summary>
    public SkillRowViewModel Totals { get; } = new();

    public void Select(ActorRowViewModel row)
    {
        ActorId = row.Id;
        HasChart = false;
        Name = row.Name;
        Accent = row.Accent;
        BarFill = row.BarFill;
        Skills.Clear();
        _rebuildingOthers = true;
        CompareWith = null;
        Others.Clear();
        _rebuildingOthers = false;
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
        Profile = ProfileText(actor);
        Target = MeterViewModel.TitleOf(snapshot);
        Dps = ActorRowViewModel.Compact(actor.Dps);
        ActiveDps = ActorRowViewModel.Compact(actor.ActiveDps);
        Total = ActorRowViewModel.Compact(actor.Damage);
        Duration = duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        Overkill = ActorRowViewModel.Compact(actor.Overkill);
        DamageTaken = ActorRowViewModel.Compact(actor.DamageTaken);
        DeathCount = actor.Deaths.ToString(culture);
        UpdateDeaths(snapshot, actor.Id);
        UpdateChart(actor.Timeline ?? []);
        ChartMarks = MarksText(snapshot.Marks);
        UpdateActivity(FightActivity.From(actor.Timeline));
        UpdateAdvice(snapshot, actor);
        UpdateComparison(snapshot, actor);

        var skills = actor.Skills;
        var top = skills.Count > 0 ? skills[0].Damage : 0;
        for (var i = 0; i < skills.Count; i++)
        {
            if (i >= Skills.Count)
            {
                Skills.Add(new SkillRowViewModel());
            }

            Skills[i].Update(names.Of(skills[i].SkillId), skills[i], top, duration);
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
    }

    private void UpdateChart(IReadOnlyList<long> timeline)
    {
        var dps = RollingDps(timeline, ChartWindowSeconds);
        HasChart = dps.Count >= 2;
        if (!HasChart)
        {
            return;
        }

        var peak = dps.Max();
        var points = new PointCollection(dps.Count + 1) { new(0, peak) };
        for (var i = 0; i < dps.Count; i++)
        {
            points.Add(new Point(i + 1, peak - dps[i]));
        }

        points.Freeze();
        ChartPoints = points;
        PeakDps = ActorRowViewModel.Compact(peak);
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

    private void UpdateComparison(EncounterSnapshot snapshot, ActorSnapshot actor)
    {
        var others = snapshot.Actors.Where(a => a.Id != actor.Id).OrderByDescending(a => a.Dps).Select(a => new CompareOption(a.Id, Display(a.Name))).ToList();
        if (!others.SequenceEqual(Others))
        {
            var selected = CompareWith?.Id;
            _rebuildingOthers = true;
            try
            {
                Others.Clear();
                foreach (var option in others)
                {
                    Others.Add(option);
                }

                // Reasignar la elección después de rearmar la lista (el ComboBox la pierde al limpiar).
                CompareWith = others.FirstOrDefault(o => o.Id == selected);
            }
            finally
            {
                _rebuildingOthers = false;
            }
        }

        var other = CompareWith is { } choice ? snapshot.Actors.FirstOrDefault(a => a.Id == choice.Id) : null;
        HasComparison = other is not null;
        Comparison.Clear();
        if (other is not null)
        {
            foreach (var row in Compare(actor, other))
            {
                Comparison.Add(row);
            }
        }
    }

    /// <summary>Las filas de la comparación entre dos jugadores de la misma pelea.</summary>
    public static IReadOnlyList<CompareRow> Compare(ActorSnapshot mine, ActorSnapshot theirs)
    {
        var culture = CultureInfo.CurrentCulture;
        var myActivity = FightActivity.From(mine.Timeline);
        var theirActivity = FightActivity.From(theirs.Timeline);
        var myCounts = mine.Counts ?? HitCounts.Empty;
        var theirCounts = theirs.Counts ?? HitCounts.Empty;
        return
        [
            Row("DPS", mine.Dps, theirs.Dps, v => ActorRowViewModel.Compact(v)),
            Row("Daño", mine.Damage, theirs.Damage, v => ActorRowViewModel.Compact(v)),
            Row("DPS activo", mine.ActiveDps, theirs.ActiveDps, v => ActorRowViewModel.Compact(v)),
            Row("Crítico", mine.CritRate, theirs.CritRate, v => v.ToString("P0", culture)),
            Row("Espalda", myCounts.Rate(myCounts.Back), theirCounts.Rate(theirCounts.Back), v => v.ToString("P0", culture)),
            Row("Golpe máximo", mine.MaxHit, theirs.MaxHit, v => ActorRowViewModel.Compact(v)),
            Row("Activo", myActivity.Uptime, theirActivity.Uptime, v => v.ToString("P0", culture)),
            Row("Recibido", mine.DamageTaken, theirs.DamageTaken, v => ActorRowViewModel.Compact(v), lowerIsBetter: true),
            Row("Muertes", mine.Deaths, theirs.Deaths, v => v.ToString("0", culture), lowerIsBetter: true),
        ];

        static CompareRow Row(string label, double a, double b, Func<double, string> format, bool lowerIsBetter = false) =>
            new(label, format(a), format(b), lowerIsBetter ? a < b : a > b, lowerIsBetter ? b < a : b > a);
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

    private void UpdateDeaths(EncounterSnapshot snapshot, ulong actorId)
    {
        var deaths = (snapshot.Deaths ?? []).Where(d => d.ActorId == actorId).ToList();
        HasDeaths = deaths.Count > 0;
        if (deaths.Count == Deaths.Count)
        {
            return; // las muertes no cambian una vez registradas
        }

        Deaths.Clear();
        foreach (var death in deaths)
        {
            Deaths.Add(Describe(death, snapshot.Start, names));
        }
    }

    /// <summary>"01:23 · Lo mató Tarka con Skill 502 por 9.0k (crítico)" y el resumen de los golpes previos.</summary>
    public static DeathItem Describe(DeathRecap death, DateTimeOffset start, SkillNames names)
    {
        var offset = death.Timestamp - start;
        var when = $"{(int)offset.TotalMinutes:00}:{offset.Seconds:00}";
        if (death.KillingBlow is not { } blow)
        {
            return new DeathItem(when, "Murió (sin golpes registrados antes)", string.Empty);
        }

        var flags = LogViewModel.Flags(blow.Modifiers);
        var summary = $"Lo mató {blow.SourceName} con {names.Of(blow.SkillId)} por {ActorRowViewModel.Compact(blow.Amount)}{(flags.Length > 0 ? $" ({flags})" : string.Empty)}";
        var total = death.LastHits.Sum(h => h.Amount);
        var lastHits = $"{death.LastHits.Count} golpes por {ActorRowViewModel.Compact(total)} en los {DeathRecap.Window.TotalSeconds:0} s anteriores";
        return new DeathItem(when, summary, lastHits);
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
