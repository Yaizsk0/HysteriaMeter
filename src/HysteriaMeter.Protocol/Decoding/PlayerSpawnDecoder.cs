using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Aparición de otro jugador (opcode 0x3645, 1,5–2 KB). Solo leemos el nombre:
/// <code>varint entity | u8 | varint ? | u8 | string name | u32 ? (¿clase o nivel?) | ... posición, legión, equipo</code>
/// El jugador local no llega por acá: su aparición se manda al loguear o al cambiar de zona.
/// </summary>
public sealed class PlayerSpawnDecoder : IPacketDecoder
{
    public PacketKind Kind => PacketKind.EntityInfo;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var entity = reader.ReadVarUInt();
        reader.Skip(1);
        reader.ReadVarUInt();
        reader.Skip(1);
        var name = reader.ReadUtf8String();
        if (PlayerNames.IsPlausible(name))
        {
            output.Add(new EntityInfoEvent(timestamp, entity, name, ClassId: null));
        }
    }
}
