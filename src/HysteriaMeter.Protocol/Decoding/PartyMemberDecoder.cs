using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// HP/MP de un miembro de la party (opcode 0x921B), cerca de una vez por segundo y por miembro:
/// <code>varint entity | varint hp | varint hpMax | u32 mp | u32 mpMax | u64 0 | u32 ? | u32 ? | u8</code>
/// Solo nos interesa la entidad: que llegue este paquete ya dice que es de la party. En la dungeon en party
/// de 5 trajo exactamente a los 4 compañeros; con 12 jugadores ajenos alrededor no llegó ninguno.
/// </summary>
public sealed class PartyMemberDecoder : IPacketDecoder
{
    public PacketKind Kind => PacketKind.PartyMember;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        output.Add(new PartyMemberSeenEvent(timestamp, reader.ReadVarUInt()));
    }
}
