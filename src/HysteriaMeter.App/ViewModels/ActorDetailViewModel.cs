using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Una muerte del jugador elegido, para la sección MUERTES del detalle.</summary>
public sealed record DeathItem(string When, string Summary, string LastHits);

/// <summary>Desglose por skill de un jugador (referencia: mocks/Detalle.dc.html). Se refresca con el overlay.</summary>
public sealed partial class ActorDetailViewModel(SkillNames names) : ObservableObject
{
    /// <summary>De dónde sale el registro de la pelea (el motor); <c>null</c> en pruebas.</summary>
    public Func<IReadOnlyList<CombatLogEntry>>? LogSource { get; set; }

    public SkillNames Names => names;

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

    [ObservableProperty]
    public partial bool HasDeaths { get; set; }

    /// <summary>Jugador elegido; <c>null</c> si no hay ninguno.</summary>
    public ulong? ActorId { get; private set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

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

        var duration = snapshot.Duration;
        var culture = CultureInfo.CurrentCulture;
        Name = actor.Name;
        Target = snapshot.TargetName ?? "Encuentro";
        Dps = ActorRowViewModel.Compact(actor.Dps);
        ActiveDps = ActorRowViewModel.Compact(actor.ActiveDps);
        Total = ActorRowViewModel.Compact(actor.Damage);
        Duration = duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        Overkill = ActorRowViewModel.Compact(actor.Overkill);
        DamageTaken = ActorRowViewModel.Compact(actor.DamageTaken);
        DeathCount = actor.Deaths.ToString(culture);
        UpdateDeaths(snapshot, actor.Id);
        UpdateChart(actor.Timeline ?? []);

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
