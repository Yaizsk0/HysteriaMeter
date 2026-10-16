using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Events;

namespace HysteriaMeter.App.ViewModels;

public sealed partial class ActorRowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial ulong Id { get; set; }

    [ObservableProperty]
    public partial int Rank { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Nombre con la marca "(vos)" para el jugador local, como en el mock.</summary>
    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    /// <summary>Letra del rombo cuando todavía no se sabe la clase: la inicial del nombre.</summary>
    [ObservableProperty]
    public partial string Initial { get; set; } = string.Empty;

    /// <summary>Símbolo de la clase dentro del rombo; <c>null</c> hasta que use una skill de su clase.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClassIcon))]
    public partial Geometry? ClassIcon { get; set; }

    public bool HasClassIcon => ClassIcon is not null;

    [ObservableProperty]
    public partial string ClassName { get; set; } = string.Empty;

    /// <summary>"2130 · 93.1k": item level y power, o vacío si no llegaron (o se ocultaron).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGear))]
    public partial string Gear { get; set; } = string.Empty;

    public bool HasGear => Gear.Length > 0;

    [ObservableProperty]
    public partial bool IsLocalPlayer { get; set; }

    /// <summary>DPS o HPS según la pestaña.</summary>
    [ObservableProperty]
    public partial string PerSecond { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Total { get; set; } = string.Empty;

    /// <summary>HPS para la vista de daño y curación juntos.</summary>
    [ObservableProperty]
    public partial string HealingPerSecond { get; set; } = string.Empty;

    /// <summary>Color propio del jugador: rombo y barra. El local siempre es dorado.</summary>
    [ObservableProperty]
    public partial Brush Accent { get; set; } = PlayerColors.Local;

    [ObservableProperty]
    public partial Brush BarFill { get; set; } = PlayerColors.LocalBar;

    [ObservableProperty]
    public partial string Share { get; set; } = string.Empty;

    /// <summary>Detalle del golpe a golpe, como las columnas de Combat Analysis (tooltip de la fila).</summary>
    [ObservableProperty]
    public partial string Breakdown { get; set; } = string.Empty;

    /// <summary>DPS de los últimos segundos como línea tenue detrás de la fila; <c>null</c> sin datos o apagado.</summary>
    [ObservableProperty]
    public partial PointCollection? Spark { get; set; }

    /// <summary>DPS de los últimos <see cref="RecentSeconds"/> segundos (columna "10 s").</summary>
    [ObservableProperty]
    public partial string Recent { get; set; } = "–";

    /// <summary>Porcentaje de golpes críticos (columna "CRIT").</summary>
    [ObservableProperty]
    public partial string Crit { get; set; } = "–";

    /// <summary>Golpe más alto de la pelea (columna "MÁX").</summary>
    [ObservableProperty]
    public partial string MaxHitText { get; set; } = "–";

    public const int RecentSeconds = 10;

    /// <summary>
    /// Daño de los últimos <paramref name="window"/> segundos de la pelea dividido por esos segundos.
    /// Cuenta hasta el final de la pelea, así quien dejó de pegar baja a 0 aunque su último golpe sea viejo.
    /// </summary>
    public static double RecentDps(IReadOnlyList<long>? timeline, TimeSpan duration, int window = RecentSeconds)
    {
        if (timeline is not { Count: > 0 })
        {
            return 0;
        }

        var end = Math.Max(timeline.Count, (int)Math.Ceiling(duration.TotalSeconds));
        var start = Math.Max(0, end - window);
        long sum = 0;
        for (var i = start; i < Math.Min(end, timeline.Count); i++)
        {
            sum += timeline[i];
        }

        return (double)sum / (end - start);
    }

    /// <summary>Segundos que muestra la línea de cada fila.</summary>
    public const int SparkSeconds = 30;

    /// <summary>
    /// Puntos de la línea de DPS: promedio de 3 s de los últimos <see cref="SparkSeconds"/> segundos, con el máximo arriba.
    /// La vista la estira al ancho de la fila.
    /// </summary>
    public static PointCollection? Sparkline(IReadOnlyList<long>? timeline)
    {
        if (timeline is not { Count: >= 3 })
        {
            return null;
        }

        var dps = ActorDetailViewModel.RollingDps(timeline, 3);
        var recent = dps.Skip(Math.Max(0, dps.Count - SparkSeconds)).ToList();
        var peak = recent.Max();
        if (peak <= 0)
        {
            return null;
        }

        var points = new PointCollection(recent.Count);
        for (var i = 0; i < recent.Count; i++)
        {
            points.Add(new System.Windows.Point(i, peak - recent[i]));
        }

        points.Freeze();
        return points;
    }

    /// <summary>Largo de la barra: proporción respecto del primero (0..1).</summary>
    [ObservableProperty]
    public partial double Bar { get; set; }

    public void Update(int rank, ActorSnapshot actor, MeterTab tab, long top, long total, TimeSpan duration, bool bossOnly = false, bool showGear = true, bool colorByClass = false)
    {
        var playerClass = PlayerClasses.Of(actor.ClassId);
        ClassIcon = playerClass?.Icon;
        ClassName = playerClass?.Name ?? string.Empty;
        Gear = showGear ? GearText(actor.ItemLevel, actor.CombatPower) : string.Empty;
        var value = tab.Metric(actor, bossOnly);

        Id = actor.Id;
        Rank = rank;
        Name = actor.Name;
        DisplayName = (actor.IsLocalPlayer ? $"{actor.Name} (vos)" : actor.Name) + DeathMark(actor.Deaths);
        Initial = InitialOf(actor.Name);
        if (colorByClass && playerClass is not null)
        {
            Accent = PlayerColors.ForClass(playerClass.Color);
            BarFill = PlayerColors.BarForClass(playerClass.Color);
        }
        else
        {
            Accent = actor.IsLocalPlayer ? PlayerColors.Local : PlayerColors.For(actor.Name);
            BarFill = actor.IsLocalPlayer ? PlayerColors.LocalBar : PlayerColors.BarFor(actor.Name);
        }

        IsLocalPlayer = actor.IsLocalPlayer;
        PerSecond = Compact(tab switch
        {
            MeterTab.Damage when bossOnly => actor.MainTargetDps,
            MeterTab.Damage => actor.Dps,
            _ => value / duration.TotalSeconds,
        });
        Total = Compact(value);
        Share = FormatShare(total == 0 ? 0 : (double)value / total);
        Bar = top == 0 ? 0 : (double)value / top;
        Breakdown = BuildBreakdown(actor);
    }

    /// <summary>"2130 · 93.1k" (item level · power); solo lo que se sepa.</summary>
    public static string GearText(int? itemLevel, long? combatPower) =>
        (itemLevel, combatPower) switch
        {
            ({ } level, { } power) => $"{level.ToString(CultureInfo.CurrentCulture)} · {Compact(power)}",
            ({ } level, null) => level.ToString(CultureInfo.CurrentCulture),
            (null, { } power) => Compact(power),
            _ => string.Empty,
        };

    /// <summary>" ✝" o " ✝3" detrás del nombre de quien murió en la pelea.</summary>
    public static string DeathMark(int deaths) => deaths switch
    {
        <= 0 => string.Empty,
        1 => " ✝",
        _ => $" ✝{deaths}",
    };

    /// <summary>"52%" sin decimales ni espacio: entra en la columna angosta del overlay.</summary>
    internal static string FormatShare(double share) =>
        (share * 100).ToString("0", CultureInfo.CurrentCulture) + "%";

    internal static string InitialOf(string name) =>
        name.Length > 0 && char.IsLetter(name[0]) ? char.ToUpper(name[0], CultureInfo.CurrentCulture).ToString() : "?";

    internal static string BuildBreakdown(ActorSnapshot actor)
    {
        var c = actor.Counts ?? HitCounts.Empty;
        var culture = CultureInfo.CurrentCulture;
        string Rate(HitModifiers modifier, int count) =>
            ModifierCoverage.IsIdentified(modifier) ? c.Rate(count).ToString("P0", culture) : "sin identificar";

        return string.Join(
            Environment.NewLine,
            $"Contra el objetivo: {Compact(actor.MainTargetDamage)} ({Compact(actor.MainTargetDps)} DPS)",
            $"DPS activo: {Compact(actor.ActiveDps)} · DoT {(actor.Damage == 0 ? 0 : (double)actor.PeriodicDamage / actor.Damage).ToString("P0", culture)} del daño",
            $"Golpes al objetivo: {c.Hits.ToString("N0", culture)} · máximo {Compact(actor.MaxHit)}",
            $"Crítico {Rate(HitModifiers.Critical, c.Critical)} · Perfecto {Rate(HitModifiers.Perfect, c.Perfect)} · Doble {Rate(HitModifiers.DoubleHit, c.DoubleHit)}",
            $"Frente {Rate(HitModifiers.FrontAttack, c.Front)} · Espalda {Rate(HitModifiers.BackAttack, c.Back)} · Multi {Rate(HitModifiers.MultiHit, c.MultiHit)}",
            $"Overkill: {Compact(actor.Overkill)} (daño que pasó la vida que le quedaba al objetivo)",
            $"Recibido: {Compact(actor.DamageTaken)} en {actor.HitsTaken.ToString("N0", culture)} golpes · Muertes: {actor.Deaths.ToString(culture)}");
    }

    internal static string Compact(double value) => Numbers.Format(value);
}
