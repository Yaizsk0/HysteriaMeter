using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Actualización de un recurso (opcode 0x8D00). Para la vida:
/// <code>varint entity | 02 01 00 | u32 HP</code>
/// Con otro prefijo es otro recurso (maná, etc.) y se ignora.
/// </summary>
public sealed class HealthDecoder : IPacketDecoder
{
    public PacketKind Kind => PacketKind.Health;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var entity = reader.ReadVarUInt();
        if (reader.ReadByte() != 0x02 || reader.ReadByte() != 0x01 || reader.ReadByte() != 0x00)
        {
            return;
        }

        output.Add(new HealthEvent(timestamp, entity, reader.ReadUInt32()));
    }
}
