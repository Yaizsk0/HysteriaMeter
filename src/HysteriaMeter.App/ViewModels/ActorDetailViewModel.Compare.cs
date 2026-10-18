using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Otro jugador de la pelea o, con <paramref name="Saved"/>, uno de tus intentos guardados contra el mismo objetivo.</summary>
/// <param name="SavedEncounter">La pelea guardada entera (hora, kill, muertes), para la vista Comparar.</param>
/// <param name="Label">"Tu mejor intento" sin el DPS, para los títulos de la vista Comparar.</param>
public sealed record CompareOption(
    ulong Id, string Name, ActorSnapshot? Saved = null, IReadOnlyList<HealthPhase>? SavedPhases = null, EncounterSnapshot? SavedEncounter = null, string? Label = null)
{
    /// <summary>Ids reservados para los intentos guardados: no chocan con entidades del juego.</summary>
    public const ulong PreviousAttemptId = ulong.MaxValue;

    public const ulong BestAttemptId = ulong.MaxValue - 1;

    /// <summary>"Sin comparar": la opción para volver al detalle normal.</summary>
    public const ulong NoComparisonId = ulong.MaxValue - 2;

    public static CompareOption None { get; } = new(NoComparisonId, "Sin comparar");

    public bool IsNone => Id == NoComparisonId;

    /// <summary>"Tu intento anterior · 12.3k" con tu fila de esa pelea; <c>null</c> si la pelea no tiene jugador local.</summary>
    public static CompareOption? Attempt(ulong id, string label, EncounterSnapshot? saved) =>
        saved?.Actors.FirstOrDefault(a => a.IsLocalPlayer) is { } me
            ? new CompareOption(id, $"{label} · {ActorRowViewModel.Compact(me.Dps)}", me, saved.Phases, saved, label)
            : null;

    public override string ToString() => Name;
}

/// <summary>Una fila de la comparación: qué se mide, el valor de cada uno y si el elegido gana.</summary>
public sealed record CompareRow(string Label, string Mine, string Theirs, bool MineBetter, bool TheirsBetter);

/// <summary>Vista Comparar (mock Comparar): el cuerpo del detalle contra otro jugador o contra un intento guardado.</summary>
public sealed partial class ActorDetailViewModel
{
    /// <summary>Los demás jugadores de la pelea, tus intentos guardados y "Sin comparar", para el selector "Comparar con".</summary>
    public ObservableCollection<CompareOption> Others { get; } = [];

    [ObservableProperty]
    public partial CompareOption? CompareWith { get; set; }

    /// <summary>Las stats de siempre (DPS, daño, crítico…), en la franja de abajo de la vista Comparar.</summary>
    public ObservableCollection<CompareRow> Comparison { get; } = [];

    /// <summary>Carga tu intento anterior y tu mejor intento contra el objetivo de la pelea, para compararte; <c>null</c> sin historial.</summary>
    public Func<EncounterSnapshot, Task<IReadOnlyList<CompareOption>>>? PastAttempts { get; set; }

    private IReadOnlyList<CompareOption> _past = [];
    private Guid? _pastFor;

    /// <summary>Hay con quién comparar: el cuerpo del detalle pasa a la vista Comparar y ninguna pestaña queda marcada.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSkillsTab), nameof(IsDeathsTab), nameof(IsLogTab))]
    public partial bool HasComparison { get; set; }

    // Mientras se rearma la lista de jugadores, el ComboBox escribe null en CompareWith: no hay que reaccionar.
    private bool _rebuildingOthers;

    partial void OnCompareWithChanged(CompareOption? value)
    {
        if (!_rebuildingOthers && _last is { } snapshot)
        {
            Update(snapshot);
        }
    }

    /// <summary>"ESTE INTENTO · HOY 21:37" o el nombre del jugador, en mayúsculas.</summary>
    [ObservableProperty]
    public partial string MineLabel { get; set; } = string.Empty;

    /// <summary>"TU MEJOR INTENTO · 12 OCT" o el nombre del otro jugador.</summary>
    [ObservableProperty]
    public partial string TheirsLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MineDps { get; set; } = "–";

    [ObservableProperty]
    public partial string TheirsDps { get; set; } = "–";

    /// <summary>"−2,7%": cuánto más (o menos) DPS que el otro lado.</summary>
    [ObservableProperty]
    public partial Delta CompareDelta { get; set; } = Delta.None;

    /// <summary>"Kromede · kill 4:12 · 3 muertes".</summary>
    [ObservableProperty]
    public partial string MineFight { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TheirsFight { get; set; } = string.Empty;

    /// <summary>Leyenda del gráfico: "Este intento" contra "Tu mejor intento", o los dos nombres.</summary>
    [ObservableProperty]
    public partial string MineLegend { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TheirsLegend { get; set; } = string.Empty;

    /// <summary>Encabezados de la tabla: "ESTE" y "MEJOR"/"ANTERIOR", o los nombres de los dos jugadores.</summary>
    [ObservableProperty]
    public partial string MineColumn { get; set; } = "ESTE";

    [ObservableProperty]
    public partial string TheirsColumn { get; set; } = string.Empty;

    [ObservableProperty]
    public partial CompareChartData CompareChart { get; set; } = CompareChartData.Empty;

    public ObservableCollection<CompareSkillRow> CompareSkills { get; } = [];

    /// <summary>"Perdiste más en <b>Rage Burst</b>: 4 golpes menos (−3.6M)." en tres partes, para resaltar la skill.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInsight))]
    public partial string InsightSkill { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InsightLead { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InsightTail { get; set; } = string.Empty;

    public bool HasInsight => InsightSkill.Length > 0;

    private async Task LoadPastAttemptsAsync(EncounterSnapshot snapshot)
    {
        _pastFor = snapshot.Id;
        _past = [];
        _pastDps = [];
        if (PastAttempts is null && PastDps is null)
        {
            return;
        }

        var past = PastAttempts is { } attempts ? await attempts(snapshot).ConfigureAwait(true) : [];
        var dps = PastDps is { } history ? await history(snapshot).ConfigureAwait(true) : [];

        // Si mientras cargaba se eligió otro jugador o empezó otra pelea, esto ya no corresponde.
        if (_pastFor == snapshot.Id && _last is { } last)
        {
            _past = past;
            _pastDps = dps;
            Update(last);
        }
    }

    private void UpdateComparison(EncounterSnapshot snapshot, ActorSnapshot actor)
    {
        // Tus intentos guardados primero y después los jugadores, como el mock.
        var others = (actor.IsLocalPlayer ? _past : [])
            .Concat(snapshot.Actors.Where(a => a.Id != actor.Id).OrderByDescending(a => a.Dps).Select(a => new CompareOption(a.Id, Display(a.Name))))
            .ToList();
        if (others.Count > 0)
        {
            others.Add(CompareOption.None);
        }

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

        var choice = CompareWith is { IsNone: false } picked ? picked : null;
        var other = choice is null ? null : choice.Saved ?? snapshot.Actors.FirstOrDefault(a => a.Id == choice.Id);
        HasComparison = other is not null;
        Comparison.Clear();
        if (choice is null || other is null)
        {
            return;
        }

        var rows = Compare(actor, other);
        if (choice.Saved is not null)
        {
            rows = [.. rows, .. ComparePhases(snapshot.Phases, choice.SavedPhases)];
        }

        foreach (var row in rows)
        {
            Comparison.Add(row);
        }

        UpdateCompareView(snapshot, actor, choice, other);
    }


    /// <summary>
    /// Contra un intento guardado: a qué tiempo llegó el boss a cada fase en esta pelea y en esa (antes es mejor).
    /// Solo las fases que tienen las dos.
    /// </summary>
    public static IReadOnlyList<CompareRow> ComparePhases(IReadOnlyList<HealthPhase>? mine, IReadOnlyList<HealthPhase>? theirs)
    {
        if (mine is null || theirs is null)
        {
            return [];
        }

        return
        [
            .. mine.Join(theirs, p => p.Percent, p => p.Percent, (a, b) => new CompareRow(
                $"Boss al {a.Percent}%",
                Clock(a.Second),
                Clock(b.Second),
                a.Second < b.Second,
                b.Second < a.Second)),
        ];

        static string Clock(int seconds) => TimeSpan.FromSeconds(seconds).ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>Las filas de la comparación entre dos jugadores de la misma pelea.</summary>
    public static IReadOnlyList<CompareRow> Compare(ActorSnapshot mine, ActorSnapshot theirs)
    {
        var culture = CultureInfo.CurrentCulture;
        var myActivity = FightActivity.Of(mine);
        var theirActivity = FightActivity.Of(theirs);
        var myCounts = mine.Counts ?? HitCounts.Empty;
        var theirCounts = theirs.Counts ?? HitCounts.Empty;
        return
        [
            Row("DPS", mine.Dps, theirs.Dps, v => ActorRowViewModel.Compact(v)),
            Row("Daño", mine.Damage, theirs.Damage, v => ActorRowViewModel.Compact(v)),
            // 0 es "sin golpes directos" (todo DoT), no "peor": contra un "–" nadie es mejor.
            mine.ActiveDps > 0 && theirs.ActiveDps > 0
                ? Row("DPS activo", mine.ActiveDps, theirs.ActiveDps, v => ActorRowViewModel.Compact(v))
                : new CompareRow("DPS activo", ActorRowViewModel.ActiveDpsText(mine.ActiveDps), ActorRowViewModel.ActiveDpsText(theirs.ActiveDps), false, false),
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

    private void UpdateCompareView(EncounterSnapshot snapshot, ActorSnapshot mine, CompareOption choice, ActorSnapshot theirs)
    {
        var culture = CultureInfo.CurrentCulture;
        var attempt = choice.Saved is not null;
        var theirFight = choice.SavedEncounter ?? snapshot;
        var theirName = attempt ? choice.Label ?? choice.Name : choice.Name;
        var now = Now();
        MineLabel = attempt ? $"ESTE INTENTO · {WhenLabel(snapshot.Start, now)}" : Name.ToUpper(culture);
        TheirsLabel = attempt && choice.SavedEncounter is { } saved
            ? $"{theirName.ToUpper(culture)} · {WhenLabel(saved.Start, now)}"
            : theirName.ToUpper(culture);
        MineDps = ActorRowViewModel.Compact(mine.Dps);
        TheirsDps = ActorRowViewModel.Compact(theirs.Dps);
        CompareDelta = RelativeDelta(mine.Dps, theirs.Dps);
        MineFight = FightLine(snapshot, mine);
        TheirsFight = FightLine(theirFight, theirs);
        MineLegend = attempt ? "Este intento" : Name;
        TheirsLegend = theirName;
        MineColumn = attempt ? "ESTE" : Name.ToUpper(culture);
        TheirsColumn = !attempt ? theirName.ToUpper(culture) : choice.Id == CompareOption.BestAttemptId ? "MEJOR" : "ANTERIOR";
        var mineDeaths = DeathSeconds(snapshot, mine.Id);
        var theirDeaths = DeathSeconds(theirFight, theirs.Id);
        CompareChart = new CompareChartData(
            CumulativeDps(mine.Timeline ?? [], Math.Max((int)snapshot.Duration.TotalSeconds, mineDeaths.DefaultIfEmpty().Max())),
            CumulativeDps(theirs.Timeline ?? [], Math.Max((int)theirFight.Duration.TotalSeconds, theirDeaths.DefaultIfEmpty().Max())),
            mineDeaths,
            theirDeaths);
        var skills = SkillDiff(mine, snapshot.Duration, theirs, theirFight.Duration, names);
        Replace(CompareSkills, skills);
        (InsightLead, InsightSkill, InsightTail) = Insight(mine, theirs, names, mine.IsLocalPlayer ? null : Name, mineDeaths, (int)snapshot.Duration.TotalSeconds);
    }

    /// <summary>"HOY 21:37" si la pelea fue hoy; si no, "12 OCT".</summary>
    public static string WhenLabel(DateTimeOffset start, DateTimeOffset now)
    {
        var culture = CultureInfo.CurrentCulture;
        var local = start.ToLocalTime();
        return local.Date == now.ToLocalTime().Date
            ? $"HOY {local.ToString("HH:mm", culture)}"
            : local.ToString("d MMM", culture).Replace(".", string.Empty, StringComparison.Ordinal).ToUpper(culture);
    }

    /// <summary>"Kromede · kill 4:12 · 3 muertes" (o "sin kill"), con las muertes de ese jugador.</summary>
    public static string FightLine(EncounterSnapshot fight, ActorSnapshot actor)
    {
        var length = DungeonSummaryViewModel.Length(fight.MainTargetDuration ?? fight.Duration);
        var outcome = fight.TargetKilled ? $"kill {length}" : $"sin kill · {length}";
        var deaths = actor.Deaths == 1 ? "1 muerte" : $"{actor.Deaths.ToString(CultureInfo.CurrentCulture)} muertes";
        return $"{MeterViewModel.TitleOf(fight)} · {outcome} · {deaths}";
    }

    /// <summary>"+4,2%" o "−2,7%": la diferencia de DPS respecto del otro lado (verde si es mejor).</summary>
    public static Delta RelativeDelta(double mine, double theirs)
    {
        if (theirs <= 0)
        {
            return Delta.None;
        }

        var change = (mine - theirs) / theirs;
        var tone = change >= 0.0005 ? DeltaTone.Better : change <= -0.0005 ? DeltaTone.Worse : DeltaTone.Neutral;
        return new Delta(change.ToString("+0.0%;−0.0%;0.0%", CultureInfo.CurrentCulture), tone);
    }

    /// <summary>
    /// DPS acumulado de cada segundo: todo el daño hasta ahí dividido por los segundos que pasaron. Después del último
    /// golpe sigue contando el tiempo hasta <paramref name="lastSecond"/>; sin timeline no se inventa una curva.
    /// </summary>
    public static IReadOnlyList<double> CumulativeDps(IReadOnlyList<long> damageBySecond, int lastSecond = -1)
    {
        if (damageBySecond.Count == 0)
        {
            return [];
        }

        var result = new double[Math.Max(damageBySecond.Count, lastSecond + 1)];
        long sum = 0;
        for (var i = 0; i < result.Length; i++)
        {
            if (i < damageBySecond.Count)
            {
                sum += damageBySecond[i];
            }

            result[i] = (double)sum / (i + 1);
        }

        return result;
    }

    private static int[] DeathSeconds(EncounterSnapshot fight, ulong actorId) =>
        [.. (fight.Deaths ?? []).Where(d => d.ActorId == actorId).Select(d => (int)Math.Max(0, (d.Timestamp - fight.Start).TotalSeconds))];

    /// <summary>
    /// Daño y golpes por skill de cada lado (agrupados por nombre, así las variantes de una skill suman juntas), de la que
    /// más te pegó a la que menos. La barra es la diferencia de daño respecto del otro: un 25 % ya la llena.
    /// </summary>
    public static IReadOnlyList<CompareSkillRow> SkillDiff(ActorSnapshot mine, TimeSpan mineDuration, ActorSnapshot theirs, TimeSpan theirDuration, SkillNames names)
    {
        var culture = CultureInfo.CurrentCulture;
        var a = Group(mine, names);
        var b = Group(theirs, names);
        return
        [
            .. a.Keys.Union(b.Keys)
                .Select(name => (Name: name, Mine: a.GetValueOrDefault(name), Theirs: b.GetValueOrDefault(name)))
                .OrderByDescending(s => s.Mine.Damage)
                .ThenByDescending(s => s.Theirs.Damage)
                .Select(s =>
                {
                    var diff = s.Theirs.Damage == 0 ? (s.Mine.Damage > 0 ? 1 : 0) : (double)(s.Mine.Damage - s.Theirs.Damage) / s.Theirs.Damage;
                    var bar = Math.Min(Math.Abs(diff) * 4, 1);
                    var tone = diff >= 0.005 ? DeltaTone.Better : diff <= -0.005 ? DeltaTone.Worse : DeltaTone.Neutral;
                    var hitsDelta = s.Mine.Hits - s.Theirs.Hits;
                    var hits = $"{s.Mine.Hits.ToString(culture)} / {s.Theirs.Hits.ToString(culture)}"
                        + (hitsDelta == 0 ? string.Empty : $" ({hitsDelta.ToString("+0;−0", culture)})");
                    var tip = $"{s.Name}: {ActorRowViewModel.Compact(s.Mine.Damage)} contra {ActorRowViewModel.Compact(s.Theirs.Damage)}"
                        + (s.Theirs.Damage == 0 ? string.Empty : $" ({diff.ToString("+0.0%;−0.0%;0.0%", culture)})")
                        + $" · DPS {ActorRowViewModel.Compact(s.Mine.Damage / Math.Max(1, mineDuration.TotalSeconds))}"
                        + $" contra {ActorRowViewModel.Compact(s.Theirs.Damage / Math.Max(1, theirDuration.TotalSeconds))}";
                    return new CompareSkillRow(
                        s.Name,
                        names.IconOf(s.Mine.SkillId ?? s.Theirs.SkillId ?? 0),
                        ActorRowViewModel.Compact(s.Mine.Damage),
                        ActorRowViewModel.Compact(s.Theirs.Damage),
                        tone == DeltaTone.Better ? bar : 0,
                        tone == DeltaTone.Worse ? bar : 0,
                        hits,
                        tone,
                        tip);
                }),
        ];
    }

    /// <summary>
    /// La skill donde más daño se perdió contra el otro lado ("Perdiste más en X: 4 golpes menos (−3.6M).") o, si no se
    /// perdió en ninguna, donde más se ganó. Vacío si no hay diferencias. <paramref name="who"/> es el nombre del jugador
    /// si no sos vos. Si se perdió y el jugador murió en la pelea (<paramref name="deathSeconds"/>), agrega el tramo
    /// muerto como el mock: ", coincide con la muerte de 1:52 a 2:20".
    /// </summary>
    public static (string Lead, string Skill, string Tail) Insight(
        ActorSnapshot mine, ActorSnapshot theirs, SkillNames names, string? who = null, IReadOnlyList<int>? deathSeconds = null, int fightSeconds = 0)
    {
        var a = Group(mine, names);
        var b = Group(theirs, names);
        var diffs = a.Keys.Union(b.Keys)
            .Select(name => (Name: name, Damage: a.GetValueOrDefault(name).Damage - b.GetValueOrDefault(name).Damage, Hits: a.GetValueOrDefault(name).Hits - b.GetValueOrDefault(name).Hits))
            .Where(d => d.Damage != 0)
            .ToList();
        if (diffs.Count == 0)
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        var lost = diffs.MinBy(d => d.Damage);
        var pick = lost.Damage < 0 ? lost : diffs.MaxBy(d => d.Damage);
        var lead = (pick.Damage < 0, who) switch
        {
            (true, null) => "Perdiste más en ",
            (true, _) => $"{who} perdió más en ",
            (false, null) => "Sacaste más ventaja en ",
            (false, _) => $"{who} sacó más ventaja en ",
        };
        var hits = pick.Hits switch
        {
            0 => "los mismos golpes",
            1 => "1 golpe más",
            -1 => "1 golpe menos",
            > 0 => $"{pick.Hits.ToString(CultureInfo.CurrentCulture)} golpes más",
            _ => $"{(-pick.Hits).ToString(CultureInfo.CurrentCulture)} golpes menos",
        };
        var amount = (pick.Damage < 0 ? "−" : "+") + ActorRowViewModel.Compact(Math.Abs(pick.Damage));
        // Sin los ticks de DoT: un veneno que sigue pegando con el jugador muerto no es la vuelta. Las peleas sin DoT
        // (o guardadas antes de la serie directa) usan el Timeline, que en ese caso es lo mismo.
        var death = pick.Damage < 0 ? DeathClause(deathSeconds ?? [], mine.DirectTimeline ?? mine.Timeline, fightSeconds) : string.Empty;
        return (lead, pick.Name, $": {hits} ({amount}){death}.");
    }

    /// <summary>Tramo muerto mínimo para nombrarlo: si volvió a pegar enseguida, la muerte no explica la diferencia.</summary>
    public const int MinDeathSpanSeconds = 3;

    /// <summary>
    /// ", coincide con la muerte de 1:52 a 2:20": el tramo muerto más largo, de la muerte al siguiente golpe directo de
    /// <paramref name="timeline"/> (la reaparición: se le pasa la serie sin DoT) o, si no volvió a pegar, al final de la
    /// pelea. Vacío sin muertes o si ningún tramo llega a <see cref="MinDeathSpanSeconds"/>.
    /// </summary>
    public static string DeathClause(IReadOnlyList<int> deathSeconds, IReadOnlyList<long>? timeline, int fightSeconds)
    {
        var spans = deathSeconds.Select(death =>
        {
            var back = timeline is null ? -1 : Enumerable.Range(death + 1, Math.Max(0, timeline.Count - death - 1)).FirstOrDefault(i => timeline[i] > 0, -1);
            return (Start: death, End: back >= 0 ? back : Math.Max(fightSeconds, death));
        }).Where(s => s.End - s.Start >= MinDeathSpanSeconds).ToList();
        if (spans.Count == 0)
        {
            return string.Empty;
        }

        var longest = spans.MaxBy(s => s.End - s.Start);
        return $", coincide con la muerte de {Clock(longest.Start)} a {Clock(longest.End)}";

        static string Clock(int seconds) => TimeSpan.FromSeconds(seconds).ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, (long Damage, int Hits, uint? SkillId)> Group(ActorSnapshot actor, SkillNames names) =>
        actor.Skills.GroupBy(s => names.Of(s.SkillId), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.Sum(s => s.Damage), g.Sum(s => s.Hits), (uint?)g.MaxBy(s => s.Damage)!.SkillId), StringComparer.Ordinal);
}
