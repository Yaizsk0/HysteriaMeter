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

    /// <summary>Nombre con la marca "(vos)" para el jugador local, como en el mock.</summary>
    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    /// <summary>Letra del rombo de clase. Mientras no decodifiquemos la clase, es la inicial del nombre.</summary>
    [ObservableProperty]
    public partial string Initial { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLocalPlayer { get; set; }

    /// <summary>DPS o HPS según la pestaña.</summary>
    [ObservableProperty]
    public partial string PerSecond { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Total { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Share { get; set; } = string.Empty;

    /// <summary>Detalle del golpe a golpe, como las columnas de Combat Analysis (tooltip de la fila).</summary>
    [ObservableProperty]
    public partial string Breakdown { get; set; } = string.Empty;

    /// <summary>Largo de la barra: proporción respecto del primero (0..1).</summary>
    [ObservableProperty]
    public partial double Bar { get; set; }

    public void Update(int rank, ActorSnapshot actor, MeterTab tab, long top, long total, TimeSpan duration)
    {
        var value = tab == MeterTab.Healing ? actor.Healing : actor.Damage;

        Id = actor.Id;
        Rank = rank;
        Name = actor.Name;
        DisplayName = actor.IsLocalPlayer ? $"{actor.Name} (vos)" : actor.Name;
        Initial = InitialOf(actor.Name);
        IsLocalPlayer = actor.IsLocalPlayer;
        PerSecond = Compact(tab == MeterTab.Healing ? actor.Healing / duration.TotalSeconds : actor.Dps);
        Total = Compact(value);
        Share = (total == 0 ? 0 : (double)value / total).ToString("P1", CultureInfo.CurrentCulture);
        Bar = top == 0 ? 0 : (double)value / top;
        Breakdown = BuildBreakdown(actor);
    }

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
            $"Frente {Rate(HitModifiers.FrontAttack, c.Front)} · Espalda {Rate(HitModifiers.BackAttack, c.Back)} · Multi {Rate(HitModifiers.MultiHit, c.MultiHit)}");
    }

    internal static string Compact(double value) => value switch
    {
        >= 1_000_000_000 => (value / 1_000_000_000).ToString("0.00", CultureInfo.CurrentCulture) + "B",
        >= 1_000_000 => (value / 1_000_000).ToString("0.00", CultureInfo.CurrentCulture) + "M",
        >= 1_000 => (value / 1_000).ToString("0.0", CultureInfo.CurrentCulture) + "k",
        _ => value.ToString("N0", CultureInfo.CurrentCulture),
    };
}
