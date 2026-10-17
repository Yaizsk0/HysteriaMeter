using HysteriaMeter.Capture.Sources;
using SharpPcap;

namespace HysteriaMeter.App.Config;

/// <summary>Una opción del selector de placa de red: el <c>Name</c> de Npcap (vacío = automática) y lo que se muestra.</summary>
public sealed record CaptureDeviceOption(string Name, string Label);

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

    /// <summary>La placa elegida al abrir el meter: la que usa la captura hasta reiniciarlo.</summary>
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
            options.Add(new CaptureDeviceOption(saved, $"{saved} (no está conectada)"));
        }

        return options;
    }

    /// <summary>
    /// La placa que usa la captura: la elegida en Configuración si sigue conectada; si no, o si es automática, la de
    /// appsettings (<c>Capture:Device</c>), que vacía también es automática. Una placa que ya no está no corta la captura.
    /// </summary>
    public static string? Resolve(string saved, string? configured, IReadOnlyList<(string Name, string Description)> devices) =>
        saved.Length > 0 && devices.Any(d => d.Name == saved) ? saved : configured;
}
