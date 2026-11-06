using System.Globalization;
using System.IO;
using HysteriaMeter.Capture.Sources;
using HysteriaMeter.Combat;

namespace HysteriaMeter.App.Pipeline;

/// <summary>
/// Pide grabar lo capturado a un .pcap desde el overlay, para depurar diferencias con Combat Analysis.
/// Graba solo el tráfico del juego que ya se está leyendo; no cambia nada de la captura.
/// </summary>
/// <remarks>
/// Además de ● REC, que graba de acá en adelante, está la grabación retroactiva: la captura en vivo guarda en memoria
/// los últimos <see cref="RecentWindow"/> y "Guardar lo último" los vuelca a un <c>ultimo-*.pcap</c>. Si se prendió,
/// también se vuelcan solos a un <c>auto-*.pcap</c> cuando una pelea no cierra (<see cref="SaveIfMismatch"/>).
/// </remarks>
public sealed class CaptureRecorder(string folder)
{
    /// <summary>Cuánto tráfico guarda la captura en vivo para "Guardar lo último" (~0,55 MB por minuto en una dungeon).</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(10);

    /// <summary>Archivos <c>auto-*.pcap</c> que se conservan; al pasarse se borra el más viejo.</summary>
    public const int AutomaticKeep = 20;

    private readonly Lock _saveGate = new();
    private DateTime? _lastAutomatic;

    public CaptureRecorder()
        : this(Folder)
    {
    }

    public static string Folder { get; } = AppPaths.In("Grabaciones");

    /// <summary>Archivo en el que se está grabando, o <c>null</c> si la grabación está apagada.</summary>
    public string? CurrentPath { get; private set; }

    public event Action<string?>? Changed;

    /// <summary>
    /// Vuelca el buffer de la captura en vivo al archivo que se le pasa. Lo conecta el pipeline; <c>null</c> en replay o
    /// con la captura detenida.
    /// </summary>
    public Func<string, SavedPackets?>? RecentSource { get; set; }

    public bool CanSaveRecent => RecentSource is not null;

    /// <summary>El último volcado (a pedido o automático), para Configuración → Captura.</summary>
    public SavedPackets? LastSaved { get; private set; }

    /// <summary>Se guardó un volcado. Llega desde el hilo que guardó, no desde el de la UI.</summary>
    public event Action<SavedPackets>? RecentSaved;

    public void Start(DateTime now)
    {
        CurrentPath = PathFor("pelea", now);
        Changed?.Invoke(CurrentPath);
    }

    public void Stop()
    {
        CurrentPath = null;
        Changed?.Invoke(null);
    }

    /// <summary>
    /// "Guardar lo último": vuelca los últimos <see cref="RecentWindow"/> a un <c>ultimo-*.pcap</c>. <c>null</c> sin
    /// captura en vivo o si todavía no llegó tráfico del juego. Escribe el archivo: no llamarlo desde el hilo de la UI.
    /// </summary>
    public SavedPackets? SaveRecent(DateTime now)
    {
        lock (_saveGate)
        {
            return Save("ultimo", now);
        }
    }

    /// <summary>
    /// El guardado automático: vuelca a un <c>auto-*.pcap</c> si algún objetivo de la pelea no cierra (el "Cierran X de
    /// Y" de Diagnóstico), como mucho uno cada <see cref="RecentWindow"/> (las peleas del medio caen en el siguiente) y
    /// no con ● REC prendido, que ya graba la pelea. Después deja solo los <see cref="AutomaticKeep"/> más nuevos.
    /// </summary>
    public SavedPackets? SaveIfMismatch(EncounterSnapshot fight, DateTime now)
    {
        if (!HasMismatch(fight))
        {
            return null;
        }

        lock (_saveGate)
        {
            if (CurrentPath is not null || (_lastAutomatic is { } last && now - last < RecentWindow) || Save("auto", now) is not { } saved)
            {
                return null;
            }

            _lastAutomatic = now;
            DeleteOldAutomatic();
            return saved;
        }
    }

    /// <summary>Algún objetivo verificado de la pelea no cierra.</summary>
    public static bool HasMismatch(EncounterSnapshot fight) =>
        fight.Verifications?.OfType<TargetVerification>().Any(v => !v.Closes) == true;

    /// <summary>"Últimos 7 min guardados en ultimo-….pcap": lo que el archivo cubre de verdad, no siempre 10 min.</summary>
    public static string SavedText(SavedPackets saved)
    {
        var span = saved.Span < TimeSpan.FromMinutes(1)
            ? $"{Math.Max(1, (int)saved.Span.TotalSeconds)} s"
            : $"{(int)saved.Span.TotalMinutes} min";
        return $"Últimos {span} guardados en {Path.GetFileName(saved.Path)}";
    }

    private SavedPackets? Save(string prefix, DateTime now)
    {
        if (RecentSource?.Invoke(PathFor(prefix, now)) is not { } saved)
        {
            return null;
        }

        LastSaved = saved;
        RecentSaved?.Invoke(saved);
        return saved;
    }

    private string PathFor(string prefix, DateTime now) =>
        Path.Combine(folder, $"{prefix}-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.pcap");

    /// <summary>El nombre lleva la fecha, así que el orden alfabético es el cronológico. Los de ● REC y "Guardar lo último" no se tocan.</summary>
    private void DeleteOldAutomatic()
    {
        foreach (var old in Directory.GetFiles(folder, "auto-*.pcap").OrderDescending(StringComparer.Ordinal).Skip(AutomaticKeep))
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Abierto en otro programa: queda para la próxima.
            }
        }
    }
}
