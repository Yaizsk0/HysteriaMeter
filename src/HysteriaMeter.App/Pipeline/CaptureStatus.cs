namespace HysteriaMeter.App.Pipeline;

/// <summary>En qué anda la captura, para el indicador del overlay.</summary>
public enum CaptureState
{
    /// <summary>Recién arrancó y todavía no vio tráfico del juego.</summary>
    Starting,

    /// <summary>Llegan paquetes del juego.</summary>
    Capturing,

    /// <summary>Pasó un rato sin tráfico del juego (juego cerrado, otra placa de red, VPN).</summary>
    NoTraffic,

    /// <summary>La captura se cortó (por ejemplo, falta Npcap).</summary>
    Failed,
}

/// <summary>
/// Estado de la captura compartido entre el pipeline (que lo actualiza) y el overlay (que lo muestra).
/// Seguro para hilos: el pipeline escribe desde segundo plano y la UI lee desde su hilo.
/// </summary>
public sealed class CaptureStatus
{
    /// <summary>Sin tráfico del juego durante este tiempo, se avisa.</summary>
    public static readonly TimeSpan Silence = TimeSpan.FromSeconds(15);

    private readonly DateTime _startedUtc;
    private long _lastTrafficTicks;
    private volatile string? _failure;

    public CaptureStatus() : this(DateTime.UtcNow)
    {
    }

    public CaptureStatus(DateTime startedUtc) => _startedUtc = startedUtc;

    public string? Failure => _failure;

    public void MarkTraffic(DateTime utcNow) => Interlocked.Exchange(ref _lastTrafficTicks, utcNow.Ticks);

    /// <summary>Ya llegó tráfico del juego alguna vez desde que arrancó la captura (la bienvenida deja de esperar).</summary>
    public bool HasTraffic => Interlocked.Read(ref _lastTrafficTicks) != 0;

    public void Fail(string message) => _failure = message;

    public CaptureState Current(DateTime utcNow)
    {
        if (_failure is not null)
        {
            return CaptureState.Failed;
        }

        var last = Interlocked.Read(ref _lastTrafficTicks);
        if (last == 0)
        {
            return utcNow - _startedUtc < Silence ? CaptureState.Starting : CaptureState.NoTraffic;
        }

        return utcNow - new DateTime(last, DateTimeKind.Utc) < Silence ? CaptureState.Capturing : CaptureState.NoTraffic;
    }

    /// <summary>Texto corto para el tooltip del indicador y para el overlay vacío.</summary>
    public string Describe(DateTime utcNow) => Current(utcNow) switch
    {
        CaptureState.Starting => "Buscando la conexión del juego…",
        CaptureState.Capturing => "Capturando el tráfico del juego",
        CaptureState.NoTraffic => "Sin tráfico del juego: ¿está abierto AION 2?",
        _ => $"La captura se detuvo: {_failure}",
    };
}
