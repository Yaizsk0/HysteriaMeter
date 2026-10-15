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

    public NumberStyle Numbers { get; init; } = NumberStyle.Compact;

    /// <summary>Cada cuánto se redibuja el overlay, en milisegundos.</summary>
    public static IReadOnlyList<int> RefreshOptions { get; } = [100, 250, 500, 1000];

    public int RefreshMs { get; init; } = 250;

    /// <summary>Al exportar o compartir, los nombres de los demás pasan a "Jugador 1", "Jugador 2"...</summary>
    public bool AnonymizeExports { get; init; }

    /// <summary>Webhook de Discord para mandar el resumen; vacío = nunca se manda nada.</summary>
    public string DiscordWebhook { get; init; } = string.Empty;

    /// <summary>Mandar el resumen a Discord solo al derrotar un boss.</summary>
    public bool DiscordOnKill { get; init; }

    /// <summary>Línea de DPS de los últimos 30 s detrás de cada fila.</summary>
    public bool Sparklines { get; init; } = true;

    public double Opacity { get; init; } = 1.0;

    /// <summary>Opaco del todo mientras el mouse está encima, aunque la opacidad sea baja.</summary>
    public bool OpaqueOnHover { get; init; } = true;

    /// <summary>Casi transparente cuando no hay combate hace un rato.</summary>
    public bool FadeWhenIdle { get; init; }

    public ActorScope Scope { get; init; } = ActorScope.Party;

    public bool SplitEncounters { get; init; } = true;

    /// <summary>Segundos sin daño para dar una pelea por terminada.</summary>
    public static IReadOnlyList<int> IdleOptions { get; } = [5, 8, 15, 30];

    public int IdleSeconds { get; init; } = 8;

    /// <summary>Si se está mirando una pelea guardada y empieza otra, volver solo al vivo.</summary>
    public bool AutoBackToLive { get; init; } = true;

    /// <summary>Prueba de DPS contra el muñeco: segundos de pelea que se cuentan (0 = apagada).</summary>
    public static IReadOnlyList<int> PracticeOptions { get; } = [0, 60, 180, 300];

    public int PracticeSeconds { get; init; }

    // "TextFont" y no "Font": así las preferencias guardadas con la fuente vieja no pisan la nueva por defecto.
    public string TextFont { get; init; } = "Inter";
}
