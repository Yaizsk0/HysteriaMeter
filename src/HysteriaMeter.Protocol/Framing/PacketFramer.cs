using System.Buffers;
using System.Buffers.Binary;

namespace HysteriaMeter.Protocol.Framing;

/// <summary>
/// Corta un stream TCP ya reensamblado en frames <c>[varint largo][opcode][cuerpo]</c>
/// (ver <see cref="ProtocolOptions"/> para cómo se interpreta el largo).
/// Un framer por dirección de cada conexión; acumula bytes entre llamadas.
/// </summary>
public sealed class PacketFramer(ProtocolOptions options)
{
    private readonly ArrayBufferWriter<byte> _pending = new(4096);

    public PacketFramer() : this(ProtocolOptions.Default)
    {
    }

    /// <summary>Cantidad de veces que se descartó el buffer por un largo inválido.</summary>
    public int Desyncs { get; private set; }

    public IReadOnlyList<RawFrame> Push(ReadOnlySpan<byte> data)
    {
        _pending.Write(data);
        var frames = new List<RawFrame>();
        var buffer = _pending.WrittenSpan;
        var offset = 0;

        while (offset < buffer.Length)
        {
            ulong length;
            int prefix;
            try
            {
                if (!VarInt.TryRead(buffer[offset..], out length, out prefix))
                {
                    break;
                }
            }
            catch (InvalidDataException)
            {
                Desyncs++;
                offset = buffer.Length;
                break;
            }

            var bodyLength = (long)length - options.LengthBias;
            if (bodyLength < options.OpcodeSize || bodyLength > options.MaxFrameSize)
            {
                // Perdimos sincronía (o el formato asumido es incorrecto). Descartamos lo pendiente
                // y esperamos que el siguiente segmento arranque en un borde de frame.
                Desyncs++;
                offset = buffer.Length;
                break;
            }

            if (buffer.Length - offset - prefix < bodyLength)
            {
                break;
            }

            var frame = buffer.Slice(offset + prefix, (int)bodyLength);
            frames.Add(new RawFrame(ReadOpcode(frame), frame[options.OpcodeSize..].ToArray()));
            offset += prefix + (int)bodyLength;
        }

        Compact(offset);
        return frames;
    }

    public void Reset() => _pending.Clear();

    private ushort ReadOpcode(ReadOnlySpan<byte> frame) => options.OpcodeSize switch
    {
        1 => frame[0],
        2 when options.OpcodeBigEndian => BinaryPrimitives.ReadUInt16BigEndian(frame),
        2 => BinaryPrimitives.ReadUInt16LittleEndian(frame),
        _ => throw new NotSupportedException($"OpcodeSize {options.OpcodeSize} no soportado."),
    };

    private void Compact(int consumed)
    {
        if (consumed == 0)
        {
            return;
        }

        var rest = _pending.WrittenSpan[consumed..].ToArray();
        _pending.Clear();
        _pending.Write(rest);
    }
}

public readonly record struct RawFrame(ushort Opcode, ReadOnlyMemory<byte> Body);
