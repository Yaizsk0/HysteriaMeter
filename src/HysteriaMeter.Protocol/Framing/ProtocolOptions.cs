namespace HysteriaMeter.Protocol.Framing;

/// <summary>
/// Parámetros del framing. Los valores por defecto son los confirmados con capturas reales
/// del cliente global (2026-10-14): <c>[varint L][opcode u16 LE][cuerpo]</c>, donde los bytes
/// que siguen al prefijo son <c>L - 4</c>.
/// </summary>
public sealed record ProtocolOptions
{
    /// <summary>
    /// Diferencia entre el valor del prefijo y los bytes que le siguen (opcode + cuerpo).
    /// En AION 2 el largo declarado es 4 más que lo que realmente viene después del prefijo.
    /// </summary>
    public int LengthBias { get; init; } = 4;

    /// <summary>Tamaño del opcode al inicio de cada frame.</summary>
    public int OpcodeSize { get; init; } = 2;

    /// <summary>Si el opcode se lee big-endian. En AION 2 es little-endian: los bytes <c>04 38</c> son el opcode 0x3804.</summary>
    public bool OpcodeBigEndian { get; init; }

    /// <summary>Límite de seguridad: un frame más grande que esto indica que perdimos sincronía.</summary>
    public int MaxFrameSize { get; init; } = 1 << 20;

    public static ProtocolOptions Default { get; } = new();
}
