namespace HysteriaMeter.Protocol.Framing;

/// <summary>
/// Parámetros del framing. Lo publicado por otros meters indica "prefijo varint + bundles LZ4",
/// pero los detalles exactos se confirman con capturas propias; por eso son configurables.
/// </summary>
public sealed record ProtocolOptions
{
    /// <summary>Si el largo del prefijo incluye los bytes del propio prefijo.</summary>
    public bool LengthIncludesPrefix { get; init; }

    /// <summary>Tamaño del opcode al inicio de cada frame.</summary>
    public int OpcodeSize { get; init; } = 2;

    /// <summary>Si el opcode se lee big-endian (como se escribe en un hex dump: <c>06 38</c> = 0x0638).</summary>
    public bool OpcodeBigEndian { get; init; } = true;

    /// <summary>Límite de seguridad: un frame más grande que esto indica que perdimos sincronía.</summary>
    public int MaxFrameSize { get; init; } = 1 << 20;

    public static ProtocolOptions Default { get; } = new();
}
