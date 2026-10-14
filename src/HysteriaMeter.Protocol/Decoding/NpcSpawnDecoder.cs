using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Aparición de un NPC/monstruo (opcode 0x3641). Solo usamos el ID de entidad y el ID de NPC
/// para que el motor no cuente a los monstruos en el ranking:
/// <code>varint entityId | 3 bytes | u32 npcId | ... (posición, HP actual y máximo)</code>
/// </summary>
public sealed class NpcSpawnDecoder : IPacketDecoder
{
    public PacketKind Kind => PacketKind.EntitySpawn;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var entity = reader.ReadVarUInt();
        reader.Skip(3);
        var npcId = reader.ReadUInt32();
        output.Add(new EntitySpawnEvent(timestamp, entity, OwnerId: null, NpcId: npcId));
    }
}
