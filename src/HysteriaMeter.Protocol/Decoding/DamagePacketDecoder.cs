using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// PROVISORIO: el layout de abajo es una hipótesis de trabajo para tener el pipeline completo
/// funcionando de punta a punta. Hay que reemplazarlo por el layout real una vez identificado
/// con <c>hysteria-rec dump</c> sobre capturas propias.
/// <code>
/// varint sourceId | varint targetId | varint skillId | varint amount | byte modifiers
/// </code>
/// </summary>
public sealed class DamagePacketDecoder : IPacketDecoder
{
    public PacketKind Kind => PacketKind.Damage;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var source = reader.ReadVarUInt();
        var target = reader.ReadVarUInt();
        var skill = (uint)reader.ReadVarUInt();
        var amount = (long)reader.ReadVarUInt();
        var modifiers = (HitModifiers)reader.ReadByte();
        output.Add(new DamageEvent(timestamp, source, target, skill, amount, modifiers));
    }
}
