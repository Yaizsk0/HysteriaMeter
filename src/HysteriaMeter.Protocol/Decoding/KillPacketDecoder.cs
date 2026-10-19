using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Muerte de una entidad con datos del que la mató (opcode 0x8D04). De acá salen los nombres
/// de los jugadores:
/// <code>varint victim | u32 skillId | varint killer | u16 ? | string name | string legion | ...</code>
/// Los strings son UTF-8 con prefijo de largo. El nombre y la legión son del que mató.
/// </summary>
public sealed class KillPacketDecoder : IPacketDecoder
{
    public PacketKind Kind => PacketKind.Death;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var victim = reader.ReadVarUInt();
        reader.Skip(4);
        var killer = reader.ReadVarUInt();
        reader.Skip(2); // ¿servidor del que mató? Vale lo mismo que el u16 servidor de 0x3633; sin confirmar
        var name = reader.ReadUtf8String();

        output.Add(new DeathEvent(timestamp, victim));
        if (PlayerNames.IsPlausible(name))
        {
            output.Add(new EntityInfoEvent(timestamp, killer, name, ClassId: null));
        }
    }
}
