using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Actualización de valores de una entidad (opcode 0x8D00). Para la vida:
/// <code>varint entity | 02 01 00 | u32 HP</code>
/// Solo se lee ese prefijo. Cualquier otro se ignora, aunque traiga la vida: <c>02 01 07</c> es otro valor (el HP
/// máximo en el fixture de los tests), pero <c>02 02 00</c> son dos entradas, la vida (id 00) y el id 07 (ver
/// RealCapture.TwoEntryHealthBody en los tests).
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
