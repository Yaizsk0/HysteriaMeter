using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Aparición de un NPC, monstruo o invocación (opcode 0x3641):
/// <code>varint entityId | u8 tipo | u8 | u8 hasOwnerName | [string ownerName] | u32 npcId | ... (posición, HP actual y máximo)</code>
/// Las invocaciones de jugadores (tipo 0x5F) a veces traen el nombre del dueño; con eso el motor
/// les asigna el daño. Los monstruos normales traen <c>hasOwnerName = 0</c>.
/// </summary>
public sealed class NpcSpawnDecoder : IPacketDecoder
{
    public PacketKind Kind => PacketKind.EntitySpawn;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var entity = reader.ReadVarUInt();
        reader.Skip(2);
        var ownerName = reader.ReadByte() != 0 ? reader.ReadUtf8String() : null;
        var npcId = reader.ReadUInt32();
        output.Add(new EntitySpawnEvent(timestamp, entity, OwnerId: null, NpcId: npcId, OwnerName: string.IsNullOrEmpty(ownerName) ? null : ownerName));
    }
}
