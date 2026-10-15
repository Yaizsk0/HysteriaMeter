using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Opciones que el usuario cambia desde el panel de configuración del overlay.</summary>
public sealed record MeterPreferences
{
    public static IReadOnlyList<string> Fonts { get; } = ["Segoe UI", "Bahnschrift", "Calibri", "Barlow Semi Condensed"];

    public ActorScope Scope { get; init; } = ActorScope.Party;

    public bool ShowHealing { get; init; } = true;

    public bool SplitEncounters { get; init; } = true;

    public string Font { get; init; } = "Segoe UI";
}
