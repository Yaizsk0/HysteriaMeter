using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Una fila del desglose por skill, con las columnas del mock de detalle.</summary>
public sealed partial class SkillRowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Ruta del ícono de la skill; <c>null</c> si no hay.</summary>
    [ObservableProperty]
    public partial string? Icon { get; set; }

    /// <summary>ID de la skill, para renombrarla.</summary>
    public uint SkillId { get; private set; }

    /// <summary>Parte del daño que viene de ticks de DoT, si tiene.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Damage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Share { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Hits { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Critical { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Back { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MaxHit { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Average { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Dps { get; set; } = string.Empty;

    /// <summary>Largo de la barra respecto de la skill que más pegó (0..1).</summary>
    [ObservableProperty]
    public partial double Bar { get; set; }

    public void Update(string name, SkillSnapshot skill, long top, TimeSpan duration, string? icon = null)
    {
        var culture = CultureInfo.CurrentCulture;
        var counts = skill.Counts ?? HitCounts.Empty;
        SkillId = skill.SkillId;
        Name = name;
        Icon = icon;
        Note = skill.PeriodicDamage == 0 ? string.Empty : $"DoT {((double)skill.PeriodicDamage / skill.Damage).ToString("P0", culture)}";
        Damage = ActorRowViewModel.Compact(skill.Damage);
        Share = skill.Share.ToString("P1", culture);
        Hits = skill.Hits.ToString("N0", culture);
        Critical = Rate(skill.Hits, skill.Crits);
        Back = Rate(counts.Hits, counts.Back);
        MaxHit = skill.Hits == 0 ? "–" : ActorRowViewModel.Compact(skill.MaxHit);
        Average = skill.Hits == 0 ? "–" : ActorRowViewModel.Compact((double)skill.DirectDamage / skill.Hits);
        Dps = ActorRowViewModel.Compact(skill.Damage / duration.TotalSeconds);
        Bar = top == 0 ? 0 : (double)skill.Damage / top;
    }

    internal static string Rate(int hits, int count) =>
        hits == 0 ? "–" : ((double)count / hits).ToString("P0", CultureInfo.CurrentCulture);
}
