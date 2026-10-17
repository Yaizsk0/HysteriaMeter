using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HysteriaMeter.App.Pipeline;
using HysteriaMeter.App.ViewModels;

namespace HysteriaMeter.App.Welcome;

/// <summary>Link de un paso: una página ("Descargar Npcap") o una carpeta del meter ("Abrir carpeta").</summary>
public sealed record SetupAction(string Text, string Target, bool IsFolder);

/// <summary>Un paso de la lista de la bienvenida: marca (✓ o su número), título, pista y estado a la derecha.</summary>
public sealed record SetupStep(int Number, string Title, string Hint, string State, bool IsReady, SetupAction? Action = null)
{
    public string Mark => IsReady ? "✓" : Number.ToString(CultureInfo.InvariantCulture);

    public bool HasAction => Action is not null;
}

/// <summary>Lo que la bienvenida sabe de la captura sin tocar la red: si está Npcap y qué placa se configuró.</summary>
/// <param name="Device">La placa elegida en la configuración (<c>Capture:Device</c>); vacía = automática.</param>
/// <param name="ReplayFile">En modo repetición, el .pcap que se reproduce: no hay placa.</param>
public sealed record CaptureSetup(bool NpcapInstalled, string? Device, string? ReplayFile = null)
{
    /// <summary>
    /// Npcap deja wpcap.dll en System32\Npcap (y también en System32 con el modo compatible con WinPcap). Es solo
    /// mirar si el archivo existe: no abre placas ni carga el driver.
    /// </summary>
    public static bool NpcapPresent() =>
        File.Exists(Path.Combine(Environment.SystemDirectory, "Npcap", "wpcap.dll"))
        || File.Exists(Path.Combine(Environment.SystemDirectory, "wpcap.dll"));

    public static CaptureSetup From(CaptureSettings settings) => new(
        NpcapPresent(),
        string.IsNullOrWhiteSpace(settings.Device) ? null : settings.Device,
        settings.Mode == CaptureMode.Replay && settings.ReplayPath is { Length: > 0 } replay ? Path.GetFileName(replay) : null);
}

/// <summary>
/// Bienvenida del primer arranque (mock Inicio): una lista con lo que hace falta para medir (Npcap, placa de red y datos
/// del juego), con datos reales. La captura ya corre desde el arranque: "Empezar a capturar" solo la da por vista.
/// </summary>
public sealed partial class WelcomeViewModel : ObservableObject
{
    public const string NpcapDownloadUrl = "https://npcap.com/#download";

    private readonly MeterSession _session;
    private readonly CaptureSetup _capture;
    private readonly GameDataSummary _gameData;

    public WelcomeViewModel(MeterSession session, CaptureSetup capture, GameDataSummary gameData)
    {
        _session = session;
        _capture = capture;
        _gameData = gameData;
        Tips = TipsFor(session.Preferences);
        Steps = BuildSteps();
    }

    /// <summary>La ventana se cierra (los dos botones y Esc).</summary>
    public event Action? CloseRequested;

    [ObservableProperty]
    public partial IReadOnlyList<SetupStep> Steps { get; set; }

    /// <summary>Los consejos que antes iban en el banner de bienvenida del overlay, con los atajos configurados.</summary>
    public IReadOnlyList<string> Tips { get; }

    /// <summary>Vuelve a mirar la captura (llegó tráfico, se cortó). Lo llama la ventana cada segundo: solo lee memoria.</summary>
    public void Refresh()
    {
        var steps = BuildSteps();
        if (!steps.SequenceEqual(Steps))
        {
            Steps = steps;
        }
    }

    /// <summary>Da la bienvenida por vista: no vuelve a aparecer.</summary>
    [RelayCommand]
    private void Start()
    {
        _session.Update(_session.Preferences with { WelcomeDone = true });
        CloseRequested?.Invoke();
    }

    /// <summary>Cierra sin marcarla: aparece de nuevo en el próximo arranque.</summary>
    [RelayCommand]
    private void Later() => CloseRequested?.Invoke();

    [RelayCommand]
    private static void OpenAction(SetupAction? action)
    {
        if (action is null)
        {
            return;
        }

        if (action.IsFolder)
        {
            Directory.CreateDirectory(action.Target);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{action.Target}\"") { UseShellExecute = true });
            return;
        }

        // Solo abre la página en el navegador: el meter no descarga ni instala nada.
        Process.Start(new ProcessStartInfo(action.Target) { UseShellExecute = true });
    }

    public static IReadOnlyList<string> TipsFor(MeterPreferences preferences) =>
    [
        "Clic en una fila: detalle · Doble clic en el encabezado: compacto · "
            + (preferences.HotkeyClickThrough is { Length: > 0 } key
                ? $"{key}: dejar pasar clics · "
                : string.Empty)
            + "El engranaje abre la configuración.",
    ];

    private IReadOnlyList<SetupStep> BuildSteps() =>
        [NpcapStep(_capture.NpcapInstalled), DeviceStep(_capture, _session.Status), GameDataStep(_gameData)];

    public static SetupStep NpcapStep(bool installed) => installed
        ? new SetupStep(1, "Npcap instalado", "Driver de captura de red", "Listo", true)
        : new SetupStep(
            1,
            "No se encontró Npcap",
            "El meter necesita Npcap para leer la red. Instalalo con la opción \"WinPcap API-compatible mode\" y volvé a abrir el meter.",
            "Revisar",
            false,
            new SetupAction("Descargar Npcap", NpcapDownloadUrl, IsFolder: false));

    /// <summary>
    /// La placa: la elegida o "Automática" (se queda con la primera por donde llega el juego). Hasta el primer paquete
    /// del juego queda "Esperando tráfico"; si la captura se cortó, el motivo.
    /// </summary>
    public static SetupStep DeviceStep(CaptureSetup capture, CaptureStatus status)
    {
        const string Title = "Placa de red";
        if (capture.ReplayFile is { } replay)
        {
            return new SetupStep(2, Title, $"Modo repetición: {replay}", "Listo", true);
        }

        if (status.Failure is { } failure)
        {
            return new SetupStep(2, Title, failure, "Revisar", false);
        }

        var device = capture.Device;
        if (status.HasTraffic)
        {
            return new SetupStep(2, Title, device is null ? "Automática · detectada con el tráfico del juego" : $"{device} · elegida en la configuración", "Listo", true);
        }

        return new SetupStep(2, Title, device is null ? "Automática · se elige cuando llegue tráfico del juego" : $"{device} · elegida en la configuración", "Esperando tráfico", false);
    }

    public static SetupStep GameDataStep(GameDataSummary data)
    {
        const string Title = "Datos del juego (opcional)";
        var folder = DisplayPath(data.Folder);
        return data.HasSkillFile
            ? new SetupStep(3, Title, $"Nombres e íconos en {folder}: {Count(data.Skills, "skill", "skills")}, {Count(data.Icons, "ícono", "íconos")}, {Count(data.Emblems, "emblema", "emblemas")}", "Listo", true)
            : new SetupStep(3, Title, $"Nombres e íconos en {folder}: todavía no hay skills.json", "Revisar", false, new SetupAction("Abrir carpeta", data.Folder, IsFolder: true));
    }

    /// <summary>La ruta real, con %LOCALAPPDATA% en vez de la carpeta del usuario (como el mock); en modo portable, entera.</summary>
    public static string DisplayPath(string folder)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return local.Length > 0 && folder.StartsWith(local + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? "%LOCALAPPDATA%" + folder[local.Length..]
            : folder;
    }

    private static string Count(int count, string one, string many) =>
        count == 1 ? $"1 {one}" : $"{count.ToString("N0", CultureInfo.CurrentCulture)} {many}";
}
