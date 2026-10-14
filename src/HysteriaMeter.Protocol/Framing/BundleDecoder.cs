using System.Buffers.Binary;
using K4os.Compression.LZ4;

namespace HysteriaMeter.Protocol.Framing;

/// <summary>
/// Un "bundle" (opcode 0xFFFF en AION 2) es un frame cuyo cuerpo es
/// <c>[u32 LE largo descomprimido][bloque LZ4]</c>. Descomprimido contiene más frames
/// concatenados con el mismo formato.
/// </summary>
public static class BundleDecoder
{
    private const int HeaderSize = 4;

    public static byte[] Decompress(ReadOnlySpan<byte> body, int maxSize = 4 << 20)
    {
        if (body.Length < HeaderSize)
        {
            throw new InvalidDataException("Bundle sin largo descomprimido.");
        }

        var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(body);
        if (rawSize > (uint)maxSize)
        {
            throw new InvalidDataException($"Bundle de {rawSize} bytes supera el límite.");
        }

        var output = new byte[(int)rawSize];
        var written = LZ4Codec.Decode(body[HeaderSize..], output);
        if (written != output.Length)
        {
            throw new InvalidDataException($"LZ4 devolvió {written} bytes, se esperaban {output.Length}.");
        }

        return output;
    }

    public static byte[] Compress(ReadOnlySpan<byte> raw)
    {
        var target = new byte[LZ4Codec.MaximumOutputSize(raw.Length) + HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(target, (uint)raw.Length);
        var encoded = LZ4Codec.Encode(raw, target.AsSpan(HeaderSize));
        return target.AsSpan(0, HeaderSize + encoded).ToArray();
    }
}
