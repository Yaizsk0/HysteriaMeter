namespace HysteriaMeter.Capture.Sources;

/// <summary>Origen de segmentos TCP: la placa de red en vivo o un archivo .pcap grabado.</summary>
public interface IPacketSource : IAsyncDisposable
{
    IAsyncEnumerable<TcpSegment> ReadAllAsync(CancellationToken cancellationToken);
}
