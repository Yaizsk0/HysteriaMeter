using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Actualización de valores de una entidad (opcode 0x8D00), en dos bloques de entradas <c>id | valor</c>:
/// <code>varint entity | u8 flags | [u8 n | n × (u8 id | u32)] si flags &amp; 0x01 | [u8 n | n × (u8 id | u64)] si flags &amp; 0x02</code>
/// La vida es el id 00 del bloque u64; el id 07 es el HP máximo, que no se emite, y los ids del bloque u32 no están
/// identificados. Así cierran exacto todos los 0x8D00 de las seis capturas (ver "Layout de 0x8D00" en
/// <c>protocol-notes.md</c>). Sin la vida o con bits de flags desconocidos (otra forma) se ignora sin contar un error.
/// Cuenta como error del decoder que la gramática no cierre con el cuerpo, porque termina antes o porque sobran bytes, y
/// una vida mayor a <see cref="MaxHealth"/>. Es el único decoder que exige consumir el cuerpo exacto (los otros leen lo
/// suyo y no miran la cola): con la cola, un cambio de formato queda a la vista en los errores.
/// </summary>
public sealed class HealthDecoder : IPacketDecoder
{
    private const byte U32Block = 0x01;
    private const byte U64Block = 0x02;
    private const byte HealthId = 0x00;

    /// <summary>Una entrada del bloque u32: id y valor.</summary>
    private const int U32EntryBytes = 1 + 4;

    /// <summary>
    /// Tope de la vida, el mismo que cuando se leía la mitad baja del u64: en las capturas la mitad alta vale 0 en todos
    /// los valores y la mayor vida es 89.988.206. Una mayor es basura, y desde long.MaxValue / 100 desborda la cuenta de
    /// <c>TargetVerification</c> en el motor.
    /// </summary>
    private const ulong MaxHealth = uint.MaxValue;

    public PacketKind Kind => PacketKind.Health;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var entity = reader.ReadVarUInt();
        var flags = reader.ReadByte();
        if ((flags & ~(U32Block | U64Block)) != 0)
        {
            return;
        }

        if ((flags & U32Block) != 0)
        {
            reader.Skip(reader.ReadByte() * U32EntryBytes);
        }

        ulong? hp = null;
        if ((flags & U64Block) != 0)
        {
            for (var entries = reader.ReadByte(); entries > 0; entries--)
            {
                var id = reader.ReadByte();
                var value = reader.ReadUInt64();
                if (id == HealthId)
                {
                    hp = value;
                }
            }
        }

        // Se valida al final: un cuerpo cortado (corta antes, con la excepción), con bytes de más o con la vida fuera de
        // rango no deja la vida que alcanzó a leerse.
        if (reader.Remaining != 0)
        {
            throw new InvalidDataException($"0x8D00: sobran {reader.Remaining} bytes después de la gramática.");
        }

        if (hp is not { } health)
        {
            return;
        }

        if (health > MaxHealth)
        {
            throw new InvalidDataException($"0x8D00: vida fuera de rango ({health}).");
        }

        output.Add(new HealthEvent(timestamp, entity, (long)health));
    }
}
