using System.Net;

namespace HysteriaMeter.Capture;

/// <summary>Un segmento TCP ya extraído de la trama capturada.</summary>
public readonly record struct TcpSegment(
    DateTimeOffset Timestamp,
    IPEndPoint Source,
    IPEndPoint Destination,
    uint Sequence,
    bool Syn,
    bool Fin,
    bool Rst,
    ReadOnlyMemory<byte> Payload)
{
    public FlowKey Flow => new(Source, Destination);
}

/// <summary>Una dirección de una conexión TCP.</summary>
public readonly record struct FlowKey(IPEndPoint Source, IPEndPoint Destination)
{
    public FlowKey Reverse => new(Destination, Source);

    public override string ToString() => $"{Source} → {Destination}";
}
