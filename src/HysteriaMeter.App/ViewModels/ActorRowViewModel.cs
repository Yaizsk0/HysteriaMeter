using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HysteriaMeter.Combat;

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
    }

    internal static string Compact(double value) => value switch
    {
        >= 1_000_000_000 => (value / 1_000_000_000).ToString("0.00", CultureInfo.CurrentCulture) + "B",
        >= 1_000_000 => (value / 1_000_000).ToString("0.00", CultureInfo.CurrentCulture) + "M",
        >= 10_000 => (value / 1_000).ToString("0.0", CultureInfo.CurrentCulture) + "K",
        _ => value.ToString("N0", CultureInfo.CurrentCulture),
    };
}
