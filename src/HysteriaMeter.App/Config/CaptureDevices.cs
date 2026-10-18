using HysteriaMeter.App.Pipeline;
using HysteriaMeter.Capture.Sources;
using SharpPcap;

namespace HysteriaMeter.App.Config;

/// <summary>
/// Una opción del selector de placa de red: el <c>Name</c> de Npcap (vacío = automática), lo que se muestra y si Npcap la
/// ve (la guardada que ya no está queda en la lista, sin conectar).
/// </summary>
public sealed record CaptureDeviceOption(string Name, string Label, bool Connected = true);

/// <summary>De dónde salió la placa que usa la captura (ver <see cref="CaptureDevices.Resolve"/>).</summary>
public enum CaptureDeviceOrigin
{
    /// <summary>Ninguna fija: la captura se queda con la primera placa por donde llega el juego.</summary>
    Automatic,

    /// <summary>La elegida en Configuración → Captura, que sigue conectada.</summary>
    Preferences,

    /// <summary><c>Capture:Device</c> de appsettings.json.</summary>
    AppSettings,
}

/// <summary>
/// Placas de red de Configuración → Captura. La lista sale de Npcap (<see cref="LiveCaptureSource.ListDevices"/>): son
/// solo los nombres, no abre ninguna placa ni captura nada. La elegida se guarda en las preferencias y la toma la
/// captura al abrir el meter.
/// </summary>
public static class CaptureDevices
{
    public const string AutomaticLabel = "Automática";

    /// <summary>De dónde salen las placas. App pone <see cref="FromNpcap"/>; en los tests queda vacía y no se toca Npcap.</summary>
    public static Func<IReadOnlyList<(string Name, string Description)>> Source { get; set; } = () => [];

    /// <summary>
    /// La elección de las preferencias que está usando la captura (ver <see cref="AppliedChoice"/>): con otra elegida
    /// hace falta reiniciar el meter. La pone App al arrancar.
    /// </summary>
    public static string Applied { get; set; } = string.Empty;

    /// <summary>Las placas que ve Npcap; vacía si no está instalado o no responde.</summary>
    public static IReadOnlyList<(string Name, string Description)> FromNpcap()
    {
        try
        {
            return LiveCaptureSource.ListDevices();
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or BadImageFormatException or PcapException)
        {
            // Sin Npcap no hay placas para elegir; la captura avisa el error por su lado al arrancar.
            return [];
        }
    }

    /// <summary>
    /// "Automática" primero y después cada placa. Si la guardada ya no aparece (se desenchufó o se desinstaló la VPN),
    /// queda en la lista marcada, así el selector no la cambia solo.
    /// </summary>
    public static IReadOnlyList<CaptureDeviceOption> Options(IReadOnlyList<(string Name, string Description)> devices, string saved)
    {
        var options = new List<CaptureDeviceOption> { new(string.Empty, AutomaticLabel) };
        options.AddRange(devices.Select(d => new CaptureDeviceOption(d.Name, string.IsNullOrWhiteSpace(d.Description) ? d.Name : d.Description)));
        if (saved.Length > 0 && !devices.Any(d => d.Name == saved))
        {
            options.Add(new CaptureDeviceOption(saved, $"{saved} (no está conectada)", Connected: false));
        }

        return options;
    }

    /// <summary>
    /// La placa que usa la captura: la elegida en Configuración si sigue conectada; si no, o si es automática, la de
    /// appsettings (<c>Capture:Device</c>), que vacía también es automática. Una placa que ya no está no corta la captura.
    /// </summary>
    public static string? Resolve(string saved, string? configured, IReadOnlyList<(string Name, string Description)> devices) =>
        saved.Length > 0 && devices.Any(d => d.Name == saved) ? saved : configured;

    /// <summary>
    /// Lo que quedó aplicado de la placa guardada, en los términos del selector: la guardada si la captura la usa; vacío
    /// (Automática) si no estaba conectada y <see cref="Resolve"/> siguió con la de appsettings o la automática. Fuera del
    /// modo en vivo no hay placa y cuenta como aplicada la guardada: no se pide reiniciar.
    /// </summary>
    public static string AppliedChoice(string saved, CaptureMode mode, string? device) =>
        mode != CaptureMode.Live || device == saved ? saved : string.Empty;

    /// <summary>
    /// Cómo nombrar <paramref name="device"/>, la placa que quedó para la captura: la descripción de Npcap (null si no
    /// tiene o no aparece) y de dónde salió. Es de la configuración solo si es la elegida aplicada
    /// (<see cref="Applied"/>); si la guardada no estaba conectada, <see cref="Resolve"/> siguió con la de appsettings o
    /// la automática, y es eso lo que se dice.
    /// </summary>
    public static (string? Label, CaptureDeviceOrigin Origin) Describe(string applied, string? device, IReadOnlyList<(string Name, string Description)> devices)
    {
        if (string.IsNullOrWhiteSpace(device))
        {
            return (null, CaptureDeviceOrigin.Automatic);
        }

        // La misma búsqueda que LiveCaptureSource.CandidateDevices: la primera placa cuyo nombre o descripción contiene
        // lo configurado (appsettings admite "Intel"; la elegida es el nombre entero).
        var match = devices.FirstOrDefault(d =>
            d.Name.Contains(device, StringComparison.OrdinalIgnoreCase) || d.Description.Contains(device, StringComparison.OrdinalIgnoreCase));
        var label = string.IsNullOrWhiteSpace(match.Description) ? null : match.Description;
        return (label, applied.Length > 0 && applied == device ? CaptureDeviceOrigin.Preferences : CaptureDeviceOrigin.AppSettings);
    }
}
