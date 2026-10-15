using System.Globalization;
using System.IO;

namespace HysteriaMeter.App.Pipeline;

/// <summary>
/// Pide grabar lo capturado a un .pcap desde el overlay, para depurar diferencias con Combat Analysis.
/// Graba solo el tráfico del juego que ya se está leyendo; no cambia nada de la captura.
/// </summary>
public sealed class CaptureRecorder
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HysteriaMeter", "Grabaciones");

    /// <summary>Archivo en el que se está grabando, o <c>null</c> si la grabación está apagada.</summary>
    public string? CurrentPath { get; private set; }

    public event Action<string?>? Changed;

    public void Start(DateTime now)
    {
        CurrentPath = Path.Combine(Folder, $"pelea-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.pcap");
        Changed?.Invoke(CurrentPath);
    }

    public void Stop()
    {
        CurrentPath = null;
        Changed?.Invoke(null);
    }
}
