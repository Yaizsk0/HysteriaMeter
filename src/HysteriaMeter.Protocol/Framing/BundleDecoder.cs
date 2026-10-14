using K4os.Compression.LZ4;

namespace HysteriaMeter.Protocol.Framing;

/// <summary>
/// Un "bundle" es un frame cuyo cuerpo es <c>[varint largo descomprimido][bloque LZ4]</c> y que, una vez
/// descomprimido, contiene más frames concatenados con el mismo formato.
/// </summary>
public static class BundleDecoder
{
    public static byte[] Decompress(ReadOnlySpan<byte> body, int maxSize = 4 << 20)
    {
        if (!VarInt.TryRead(body, out var rawSize, out var prefix))
        {
            throw new InvalidDataException("Bundle sin largo descomprimido.");
        }

        if (rawSize > (ulong)maxSize)
        {
            throw new InvalidDataException($"Bundle de {rawSize} bytes supera el límite.");
        }

        var output = new byte[(int)rawSize];
        var written = LZ4Codec.Decode(body[prefix..], output);
        if (written != output.Length)
        {
            throw new InvalidDataException($"LZ4 devolvió {written} bytes, se esperaban {output.Length}.");
        }

        return output;
    }

    public static byte[] Compress(ReadOnlySpan<byte> raw)
    {
        var target = new byte[LZ4Codec.MaximumOutputSize(raw.Length) + VarInt.MaxBytes];
        var prefix = VarInt.Write(target, (ulong)raw.Length);
        var encoded = LZ4Codec.Encode(raw, target.AsSpan(prefix));
        return target.AsSpan(0, prefix + encoded).ToArray();
    }
}
