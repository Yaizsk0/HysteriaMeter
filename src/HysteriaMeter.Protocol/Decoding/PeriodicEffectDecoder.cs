using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Tick de un efecto periódico, DoT o HoT (opcode 0x3805):
/// <code>varint target | u8 type | varint source | varint tickSeq | u32 instance | varint amount | u32 skillId</code>
/// <c>instance</c> y <c>skillId</c> coinciden con los del 0x3804 que aplicó el efecto. Validado en una
/// dungeon: daño directo (0x3804) + ticks de daño cierran exacto contra las bajadas de HP (0x8D00) y
/// contra el total de Combat Analysis. Los ticks con <c>type = 0x00</c> tienen otro formato y no llevan monto.
/// </summary>
public sealed class PeriodicEffectDecoder : IPacketDecoder
{
    internal const byte SelfHeal = 0x09;
    internal const byte DamageTick = 0x0A;
    internal const byte Heal = 0x0B;

    public PacketKind Kind => PacketKind.PeriodicEffect;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var target = reader.ReadVarUInt();
        var type = reader.ReadByte();
        if (type is not (DamageTick or Heal or SelfHeal))
        {
            return;
        }

        var source = reader.ReadVarUInt();
        reader.ReadVarUInt(); // tickSeq
        reader.Skip(4); // instance
        var amount = (long)reader.ReadVarUInt();
        var skill = reader.ReadUInt32();

        // Las curaciones usan el mismo layout pero todavía no están validadas contra el juego.
        output.Add(type == DamageTick
            ? new DamageEvent(timestamp, source, target, skill, amount, HitModifiers.DamageOverTime)
            : new HealEvent(timestamp, source, target, skill, amount));
    }
}
