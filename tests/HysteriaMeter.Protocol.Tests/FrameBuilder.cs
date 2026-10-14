using System.Buffers.Binary;
using HysteriaMeter.Protocol.Framing;

namespace HysteriaMeter.Protocol.Tests;

/// <summary>Arma frames con el formato real: [varint (len + 4)][opcode u16 LE][cuerpo].</summary>
internal static class FrameBuilder
{
    public static byte[] Frame(ushort opcode, params byte[] body)
    {
        var length = body.Length + 2;
        var declared = (ulong)(length + ProtocolOptions.Default.LengthBias);
        var output = new byte[VarInt.SizeOf(declared) + length];
        var offset = VarInt.Write(output, declared);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(offset), opcode);
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

    // Ignora cualquier espacio en blanco, incluido el \r de los checkouts con CRLF en Windows.
    public static byte[] Hex(string hex) => Convert.FromHexString(string.Concat(hex.Where(c => !char.IsWhiteSpace(c))));
}
