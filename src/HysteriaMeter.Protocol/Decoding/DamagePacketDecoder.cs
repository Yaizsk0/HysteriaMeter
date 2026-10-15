using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Resultado de un efecto sobre un objetivo (opcode 0x3804 en el cliente global). Layout
/// confirmado con capturas reales:
/// <code>
/// varint target | u8 flags | u8 flags2 | varint source | u32 skillId | u8 seq | u8 type
/// | [u8 hitFlags, u8, u8 side si flags &amp; 0x02] | u32 instance | u32 count | varint unknown | varint value | cola
/// </code>
/// Solo los frames con <c>flags &amp; 0x04</c> llevan daño. <c>value</c> es el daño exacto que muestra
/// Combat Analysis. Modificadores, validados contra Combat Analysis en una pelea de 92 golpes
/// (cierran exacto por skill y en total):
/// <list type="bullet">
/// <item><c>type = 0x03</c>: crítico (normal = 0x02).</item>
/// <item><c>flags &amp; 0x20</c>: multi-golpe. La cola trae una lista de valores que no suman daño.</item>
/// <item><c>side</c>: 0x02 = de frente, 0x01 = por la espalda. Sin bloque extra no es ninguno de los dos.</item>
/// <item><c>hitFlags &amp; 0x04</c>: perfecto; <c>hitFlags &amp; 0x08</c>: doble (el valor sale ~2x).</item>
/// </list>
/// </summary>
public sealed class DamagePacketDecoder : IPacketDecoder
{
    internal const byte HasDamage = 0x04;
    internal const byte HasExtra = 0x02;
    internal const byte IsMultiHit = 0x20;
    internal const byte CriticalType = 0x03;
    internal const byte PerfectBit = 0x04;
    internal const byte DoubleBit = 0x08;
    internal const byte SideBack = 0x01;
    internal const byte SideFront = 0x02;

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
        reader.Skip(1); // seq
        var type = reader.ReadByte();

        var modifiers = HitModifiers.None;
        if (type == CriticalType)
        {
            modifiers |= HitModifiers.Critical;
        }

        if ((flags & IsMultiHit) != 0)
        {
            modifiers |= HitModifiers.MultiHit;
        }

        if ((flags & HasExtra) != 0)
        {
            var hitFlags = reader.ReadByte();
            reader.Skip(1);
            var side = reader.ReadByte();
            modifiers |= side switch
            {
                SideFront => HitModifiers.FrontAttack,
                SideBack => HitModifiers.BackAttack,
                _ => HitModifiers.None,
            };

            if ((hitFlags & PerfectBit) != 0)
            {
                modifiers |= HitModifiers.Perfect;
            }

            if ((hitFlags & DoubleBit) != 0)
            {
                modifiers |= HitModifiers.DoubleHit;
            }
        }

        reader.Skip(8); // instance + count
        reader.ReadVarUInt();
        var amount = (long)reader.ReadVarUInt();
        output.Add(new DamageEvent(timestamp, source, target, skill, amount, modifiers));
    }
}
