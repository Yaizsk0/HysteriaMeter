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

    /// <summary>Alto de cada fila en píxeles (Details: "bar height").</summary>
    public static IReadOnlyList<int> RowHeightOptions { get; } = [24, 30, 36];

    public int RowHeight { get; init; } = 30;

    /// <summary>Cuántas filas mostrar (0 = todas). Si quedás afuera, tu fila reemplaza a la última, como en Details!.</summary>
    public static IReadOnlyList<int> MaxRowsOptions { get; } = [0, 5, 8, 12];

    public int MaxRows { get; init; }

    /// <summary>Al exportar o compartir, los nombres de los demás pasan a "Jugador 1", "Jugador 2"...</summary>
    public bool AnonymizeExports { get; init; }

    /// <summary>Webhook de Discord para mandar el resumen; vacío = nunca se manda nada.</summary>
    public string DiscordWebhook { get; init; } = string.Empty;

    /// <summary>Mandar el resumen a Discord solo al derrotar un boss.</summary>
    public bool DiscordOnKill { get; init; }

    /// <summary>Guardar una imagen del overlay en Exportes al derrotar un boss, para tener el registro sin acordarse.</summary>
    public bool ImageOnKill { get; init; }

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

    /// <summary>Apodos por nombre real de personaje; solo cambian lo que se muestra.</summary>
    public IReadOnlyDictionary<string, string> Nicknames { get; init; } = new Dictionary<string, string>();

    /// <summary>Un sonido de Windows con las alertas: récord, prueba de DPS terminada y enrage que no se llega.</summary>
    public bool Sounds { get; init; }

    /// <summary>Item level y power de cada jugador al lado de su nombre.</summary>
    public bool ShowGear { get; init; } = true;

    /// <summary>Barras y rombo del color de la clase (como Details!) en vez de un color por jugador.</summary>
    public bool ColorByClass { get; init; }

    /// <summary>Columnas a la derecha del DPS: total (o HPS) y porcentaje.</summary>
    public bool ShowTotalColumn { get; init; } = true;

    public bool ShowShareColumn { get; init; } = true;

    /// <summary>Al soltar el overlay cerca de un borde de la pantalla o de otra ventana del meter, se pega a ese borde.</summary>
    public bool SnapWindows { get; init; } = true;

    /// <summary>Fijar posición y tamaño: el overlay no se arrastra ni se agranda sin querer, pero los botones siguen andando (Details!: "lock").</summary>
    public bool LockPosition { get; init; }

    /// <summary>Ya se prendió "solo boss" en las ventanas guardadas antes de que viniera prendido de entrada (se hace una sola vez).</summary>
    public bool BossOnlyDefaulted { get; init; }

    /// <summary>Columna "10 s": el DPS de los últimos segundos, para ver quién está pegando ahora (como el Last10DPS de ACT).</summary>
    public bool ShowRecentColumn { get; init; }

    /// <summary>Columnas de Ember/ACT: porcentaje de críticos y golpe más alto de cada jugador.</summary>
    public bool ShowCritColumn { get; init; }

    public bool ShowMaxHitColumn { get; init; }

    /// <summary>Solo tu fila, para tener el overlay mínimo mientras jugás (como el modo colapsado de Ember).</summary>
    public bool OnlyMe { get; init; }

    /// <summary>Última novedad vista (id de <c>data/novedades.json</c>); 0 = nunca abrió el meter.</summary>
    public int LastSeenNews { get; init; }

    /// <summary>
    /// Ya pasó por la bienvenida ("Empezar a capturar"). Verdadero por defecto: unas preferencias guardadas antes de
    /// que existiera son de alguien que ya usaba el meter; App.xaml.cs la pone en falso solo para quien llega sin preferencias.
    /// </summary>
    public bool WelcomeDone { get; init; } = true;

    /// <summary>Tema visual (ver <see cref="MeterThemes"/>).</summary>
    public string Theme { get; init; } = MeterThemes.Default;

    /// <summary>Nombres de skills puestos desde el detalle (ID → nombre).</summary>
    public IReadOnlyDictionary<uint, string> SkillAliases { get; init; } = new Dictionary<uint, string>();

    /// <summary>Atajos globales, como "Ctrl+Shift+L"; vacío = sin atajo.</summary>
    public string HotkeyClickThrough { get; init; } = "Ctrl+Shift+L";

    public string HotkeyReset { get; init; } = "Ctrl+Shift+R";

    public string HotkeyVisibility { get; init; } = "Ctrl+Shift+H";

    public string HotkeyMark { get; init; } = "Ctrl+Shift+M";

    public string HotkeyMinimize { get; init; } = "Ctrl+Shift+N";

    public string HotkeyPause { get; init; } = "Ctrl+Shift+P";

    /// <summary>Rota la pestaña de la ventana principal (DPS → CURA → RECIB.). Sin atajo de fábrica.</summary>
    public string HotkeyTab { get; init; } = string.Empty;

    /// <summary>
    /// Placa de red de la captura (su <c>Name</c> de Npcap); vacío = automática. Se lee al abrir el meter: un cambio
    /// se aplica al reiniciarlo.
    /// </summary>
    public string CaptureDevice { get; init; } = string.Empty;

    // "TextFont" y no "Font": así las preferencias guardadas con la fuente vieja no pisan la nueva por defecto.
    public string TextFont { get; init; } = "Inter";
}
