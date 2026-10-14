namespace HysteriaMeter.Protocol.Framing;

/// <summary>
/// Enteros de longitud variable estilo LEB128 / protobuf: 7 bits de dato por byte,
/// el bit alto indica que sigue otro byte.
/// </summary>
public static class VarInt
{
    public const int MaxBytes = 10;

    /// <returns>
    /// <c>true</c> si se leyó un valor completo; <c>false</c> si faltan bytes (el buffer terminó a mitad del varint).
    /// </returns>
    /// <exception cref="InvalidDataException">El varint supera los 10 bytes.</exception>
    public static bool TryRead(ReadOnlySpan<byte> source, out ulong value, out int bytesRead)
    {
        value = 0;
        var shift = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (i >= MaxBytes)
            {
                throw new InvalidDataException("VarInt demasiado largo.");
            }

            var b = source[i];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                bytesRead = i + 1;
                return true;
            }

            shift += 7;
        }

        bytesRead = 0;
        value = 0;
        return false;
    }

    public static int Write(Span<byte> destination, ulong value)
    {
        var i = 0;
        while (value >= 0x80)
        {
            destination[i++] = (byte)(value | 0x80);
            value >>= 7;
        }

        destination[i++] = (byte)value;
        return i;
    }

    public static int SizeOf(ulong value)
    {
        var size = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            size++;
        }

        return size;
    }
}
