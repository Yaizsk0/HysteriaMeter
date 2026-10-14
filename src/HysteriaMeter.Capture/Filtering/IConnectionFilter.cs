namespace HysteriaMeter.Capture.Filtering;

/// <summary>Decide si un segmento pertenece a la conexión del juego.</summary>
public interface IConnectionFilter
{
    bool IsGameTraffic(in TcpSegment segment);
}

/// <summary>Deja pasar todo. Útil para grabar capturas de descubrimiento.</summary>
public sealed class AcceptAllFilter : IConnectionFilter
{
    public bool IsGameTraffic(in TcpSegment segment) => true;
}

/// <summary>Filtra por puerto remoto del servidor.</summary>
public sealed class ServerPortFilter(IEnumerable<int> ports) : IConnectionFilter
{
    private readonly HashSet<int> _ports = [.. ports];

    public bool IsGameTraffic(in TcpSegment segment) =>
        _ports.Contains(segment.Source.Port) || _ports.Contains(segment.Destination.Port);
}
