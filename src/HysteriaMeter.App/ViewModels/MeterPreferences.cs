using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Opciones que el usuario cambia desde el panel de configuración del overlay.</summary>
public sealed record MeterPreferences
{
    /// <summary>Fuentes embebidas en la app (Assets/Fonts); las demás vienen con Windows.</summary>
    public static IReadOnlyList<string> BundledFonts { get; } = ["Inter", "Barlow Semi Condensed"];

    public static IReadOnlyList<string> Fonts { get; } = ["Inter", "Segoe UI", "Bahnschrift", "Barlow Semi Condensed"];

    public static IReadOnlyList<double> Opacities { get; } = [1.0, 0.9, 0.8, 0.7, 0.6, 0.5];

    /// <summary>Días que se guardan las peleas comunes (0 = siempre). Los bosses derrotados y las peleas con nota no se borran.</summary>
    public static IReadOnlyList<int> RetentionOptions { get; } = [0, 30, 90];

    public int RetentionDays { get; init; }

    public double Opacity { get; init; } = 1.0;

    /// <summary>Opaco del todo mientras el mouse está encima, aunque la opacidad sea baja.</summary>
    public bool OpaqueOnHover { get; init; } = true;

    /// <summary>Casi transparente cuando no hay combate hace un rato.</summary>
    public bool FadeWhenIdle { get; init; }

    public ActorScope Scope { get; init; } = ActorScope.Party;

    public bool SplitEncounters { get; init; } = true;

    // "TextFont" y no "Font": así las preferencias guardadas con la fuente vieja no pisan la nueva por defecto.
    public string TextFont { get; init; } = "Inter";
}
