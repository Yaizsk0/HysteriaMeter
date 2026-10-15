using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Resultado de un efecto sobre un objetivo (opcode 0x3804 en el cliente global). Layout
/// confirmado con capturas reales (913 frames sin errores):
/// <code>
/// varint target | u8 flags | u8 flags2 | varint source | u32 skillId | u8 seq | u8 type
/// | [3 bytes si flags &amp; 0x02] | u32 instance | u32 count | varint unknown | varint value | ...
/// </code>
/// Solo los frames con <c>flags &amp; 0x04</c> llevan daño; el resto son aplicaciones/ticks de
/// buffs sin valor. El <c>value</c> sigue proporcionalmente la caída de HP del objetivo (0x8D00),
/// pero todavía falta confirmar contra Combat Analysis si es exactamente el número que muestra el juego.
/// Los bits de crítico no están identificados.
/// </summary>
public sealed class DamagePacketDecoder : IPacketDecoder
{
    internal const byte HasDamage = 0x04;
    internal const byte HasExtra = 0x02;

    public PacketKind Kind => PacketKind.Damage;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var target = reader.ReadVarUInt();
        var flags = reader.ReadByte();
        reader.Skip(1); // flags2: agrega campos al final que todavía no usamos
        if ((flags & HasDamage) == 0)
        {
            return;
        }

        var source = reader.ReadVarUInt();
        var skill = reader.ReadUInt32();
        reader.Skip(2); // seq + type
        if ((flags & HasExtra) != 0)
        {
            reader.Skip(3);
        }

        reader.Skip(8); // instance + count
        reader.ReadVarUInt();
        var amount = (long)reader.ReadVarUInt();
        output.Add(new DamageEvent(timestamp, source, target, skill, amount, HitModifiers.None));
    }
}
