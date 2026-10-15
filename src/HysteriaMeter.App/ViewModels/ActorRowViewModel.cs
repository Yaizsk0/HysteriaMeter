using System.Globalization;
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

    [ObservableProperty]
    public partial bool IsLocalPlayer { get; set; }

    [ObservableProperty]
    public partial string Dps { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Damage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Share { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CritRate { get; set; } = string.Empty;

    /// <summary>Detalle del golpe a golpe, como las columnas de Combat Analysis (tooltip de la fila).</summary>
    [ObservableProperty]
    public partial string Breakdown { get; set; } = string.Empty;

    /// <summary>Largo de la barra: proporción respecto del primero (0..1).</summary>
    [ObservableProperty]
    public partial double Bar { get; set; }

    public void Update(int rank, ActorSnapshot actor, long topDamage)
    {
        Id = actor.Id;
        Rank = rank;
        Name = actor.Name;
        IsLocalPlayer = actor.IsLocalPlayer;
        Dps = Compact(actor.Dps);
        Damage = Compact(actor.Damage);
        Share = actor.Share.ToString("P0", CultureInfo.CurrentCulture);
        CritRate = actor.CritRate.ToString("P0", CultureInfo.CurrentCulture);
        Bar = topDamage == 0 ? 0 : (double)actor.Damage / topDamage;
        Breakdown = BuildBreakdown(actor);
    }

    internal static string BuildBreakdown(ActorSnapshot actor)
    {
        var c = actor.Counts ?? HitCounts.Empty;
        var culture = CultureInfo.CurrentCulture;
        string Rate(HitModifiers modifier, int count) =>
            ModifierCoverage.IsIdentified(modifier) ? c.Rate(count).ToString("P0", culture) : "sin identificar";

        return string.Join(
            Environment.NewLine,
            $"Contra el objetivo: {Compact(actor.MainTargetDamage)} ({Compact(actor.MainTargetDps)} DPS)",
            $"Golpes: {c.Hits.ToString("N0", culture)} · máximo {Compact(actor.MaxHit)}",
            $"Crítico {Rate(HitModifiers.Critical, c.Critical)} · Perfecto {Rate(HitModifiers.Perfect, c.Perfect)} · Doble {Rate(HitModifiers.DoubleHit, c.DoubleHit)}",
            $"Frente {Rate(HitModifiers.FrontAttack, c.Front)} · Espalda {Rate(HitModifiers.BackAttack, c.Back)} · Multi {Rate(HitModifiers.MultiHit, c.MultiHit)}");
    }

    internal static string Compact(double value) => value switch
    {
        >= 1_000_000_000 => (value / 1_000_000_000).ToString("0.00", CultureInfo.CurrentCulture) + "B",
        >= 1_000_000 => (value / 1_000_000).ToString("0.00", CultureInfo.CurrentCulture) + "M",
        >= 10_000 => (value / 1_000).ToString("0.0", CultureInfo.CurrentCulture) + "K",
        _ => value.ToString("N0", CultureInfo.CurrentCulture),
    };
}
