using System.Buffers.Binary;
using System.Text;

namespace HysteriaMeter.Protocol.Framing;

/// <summary>Lector secuencial sobre el cuerpo de un paquete. Pensado para escribir decoders cortos y legibles.</summary>
public ref struct PacketReader(ReadOnlySpan<byte> buffer)
{
    private readonly ReadOnlySpan<byte> _buffer = buffer;

    public int Position { get; private set; }

    public readonly int Remaining => _buffer.Length - Position;

    public ulong ReadVarUInt()
    {
        if (!VarInt.TryRead(_buffer[Position..], out var value, out var read))
        {
            throw new EndOfStreamException("VarInt incompleto.");
        }

        Position += read;
        return value;
    }

    /// <summary>Varint con codificación zigzag (enteros con signo).</summary>
    public long ReadVarInt()
    {
        var raw = ReadVarUInt();
        return (long)(raw >> 1) ^ -(long)(raw & 1);
    }

    public byte ReadByte() => Take(1)[0];

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    /// <summary>String UTF-8 con prefijo de longitud varint en bytes (así vienen los nombres de personaje).</summary>
    public string ReadUtf8String()
    {
        var bytes = checked((int)ReadVarUInt());
        return Encoding.UTF8.GetString(Take(bytes));
    }

    public ReadOnlySpan<byte> ReadBytes(int count) => Take(count);

    public void Skip(int count) => Take(count);

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count > Remaining)
        {
            throw new EndOfStreamException($"Se pidieron {count} bytes y quedan {Remaining}.");
        }

        var slice = _buffer.Slice(Position, count);
        Position += count;
        return slice;
    }
}
