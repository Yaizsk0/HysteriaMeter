using HysteriaMeter.Combat;

namespace HysteriaMeter.App.ViewModels;

/// <summary>Cómo se muestra la curación.</summary>
public enum HealingView
{
    /// <summary>Solo daño.</summary>
    Hidden,

    /// <summary>En su propia pestaña.</summary>
    Tab,

    /// <summary>Al lado del daño, en la misma fila (columna HPS).</summary>
    Combined,
}

/// <summary>Opciones que el usuario cambia desde el panel de configuración del overlay.</summary>
public sealed record MeterPreferences
{
    /// <summary>Fuentes embebidas en la app (Assets/Fonts); las demás vienen con Windows.</summary>
    public static IReadOnlyList<string> BundledFonts { get; } = ["Inter", "Barlow Semi Condensed"];

    public static IReadOnlyList<string> Fonts { get; } = ["Inter", "Segoe UI", "Bahnschrift", "Barlow Semi Condensed"];

    public static IReadOnlyList<double> Opacities { get; } = [1.0, 0.9, 0.8, 0.7, 0.6, 0.5];

    public double Opacity { get; init; } = 1.0;

    public ActorScope Scope { get; init; } = ActorScope.Party;

    public HealingView Healing { get; init; } = HealingView.Combined;

    public bool SplitEncounters { get; init; } = true;

    // "TextFont" y no "Font": así las preferencias guardadas con la fuente vieja no pisan la nueva por defecto.
    public string TextFont { get; init; } = "Inter";
}
