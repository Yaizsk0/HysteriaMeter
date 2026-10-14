using System.Buffers.Binary;
using HysteriaMeter.Protocol.Framing;

namespace HysteriaMeter.Protocol.Tests;

/// <summary>Arma frames sintéticos con el formato por defecto: [varint largo][opcode BE][cuerpo].</summary>
internal static class FrameBuilder
{
    public static byte[] Frame(ushort opcode, params byte[] body)
    {
        var length = body.Length + 2;
        var output = new byte[VarInt.SizeOf((ulong)length) + length];
        var offset = VarInt.Write(output, (ulong)length);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(offset), opcode);
        body.CopyTo(output, offset + 2);
        return output;
    }

    public static byte[] VarInts(params ulong[] values)
    {
        var buffer = new byte[values.Length * VarInt.MaxBytes];
        var offset = 0;
        foreach (var value in values)
        {
            offset += VarInt.Write(buffer.AsSpan(offset), value);
        }

        return buffer[..offset];
    }

    public static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
