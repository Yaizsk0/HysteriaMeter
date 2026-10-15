using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Aparición de otro jugador (opcode 0x3645, 1,5–2 KB). Solo leemos el nombre:
/// <code>varint entity | u8 | u16 ? | u8 | u8 07 | string name | u32 ? (¿clase o nivel?) | ... posición, legión, equipo</code>
/// El campo después de la entidad es un u16 fijo, no un varint: leído como varint, cuando su primer byte es
/// menor a 0x80 el nombre salía corrido (largo 1, "\x07").
/// El jugador local no llega por acá: su aparición se manda al loguear o al cambiar de zona.
/// </summary>
public sealed class PlayerSpawnDecoder : IPacketDecoder
{
    public PacketKind Kind => PacketKind.EntityInfo;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var entity = reader.ReadVarUInt();
        reader.Skip(5);
        var name = reader.ReadUtf8String();
        if (PlayerNames.IsPlausible(name))
        {
            output.Add(new EntityInfoEvent(timestamp, entity, name, ClassId: null));
        }
    }
}
