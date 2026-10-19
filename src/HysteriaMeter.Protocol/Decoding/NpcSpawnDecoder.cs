using System.Buffers.Binary;
using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Aparición de un NPC, monstruo o invocación (opcode 0x3641):
/// <code>varint entityId | u8 tipo | u8 | u8 hasOwnerName | [string ownerName] | u32 npcId | u8 flags | u8 | pos 3×f32 | f32 | [12 bytes si flags tiene 0x08] | 3 bytes | varint HP | varint HPmax | cola</code>
/// Las invocaciones de jugadores (tipos 0x5F, 0x1F, 0x1C y 0x2F) a veces traen el nombre del dueño; con eso el motor
/// les asigna el daño. Los monstruos normales traen <c>hasOwnerName = 0</c>. El decoder no mira el tipo. Las de un
/// bloque traen además el id de entidad del dueño en la cola, con o sin nombre (ver <see cref="ReadOwner"/> y "Dueño de
/// la invocación" en <c>protocol-notes.md</c>).
/// </summary>
public sealed class NpcSpawnDecoder : IPacketDecoder
{
    /// <summary>Bytes entre el npcId y la vida: dos u8, la posición, un f32 y tres bytes sin identificar.</summary>
    private const int BytesBeforeHealth = 2 + (4 * 4) + 3;

    /// <summary>Del dueño de la cola: el u8 fijo que sigue a <c>k1</c>, la posición (3×f32) y <c>07 02 06</c>.</summary>
    private const int BytesBeforeOwner = 1 + (3 * 4) + 3;

    /// <summary>
    /// Con este bit en el u8 que sigue al npcId vienen 12 bytes más, sin identificar, antes de la vida. En las capturas el
    /// u8 vale 0x00, 0x40 (no agrega nada) o 0x08, y el bit no depende del tipo.
    /// </summary>
    private const byte ExtraBlockFlag = 0x08;

    private const int ExtraBlockBytes = 12;

    /// <summary>La marca del bloque de la cola que trae al dueño; tiene que aparecer una sola vez.</summary>
    private static ReadOnlySpan<byte> OwnerBlockMark => [0x80, 0x75, 0xD5, 0x2A, 0xBB, 0x03, 0x00, 0x00];

    public PacketKind Kind => PacketKind.EntitySpawn;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var entity = reader.ReadVarUInt();
        reader.Skip(2);
        var ownerName = reader.ReadByte() != 0 ? reader.ReadUtf8String() : null;
        var npcId = reader.ReadUInt32();
        var afterNpcId = body[reader.Position..];
        var (hp, maxHp, tailStart) = ReadHealth(afterNpcId);
        var owner = ReadOwner(afterNpcId[tailStart..]);
        output.Add(new EntitySpawnEvent(
            timestamp, entity, OwnerId: owner != entity ? owner : null, NpcId: npcId,
            OwnerName: ownerName is not null && PlayerNames.IsPlausible(ownerName) ? ownerName : null,
            Hp: hp, MaxHp: maxHp));
    }

    /// <summary>
    /// HP y HP máximo van en posición fija después del npcId, 12 bytes más adelante con <see cref="ExtraBlockFlag"/>.
    /// Después puede venir más (casi todos los cuerpos de la dungeon y del mini boss), así que no se leen desde el final:
    /// los dos últimos varints daban basura. Las invocaciones sin posición no los traen, y si no cierran (sin máximo, o HP
    /// mayor al máximo) se ignoran. Devuelve también dónde empieza la cola: después de los dos varints aunque la vida no
    /// cierre, o al final si no se pudieron leer.
    /// </summary>
    private static (long? Hp, long? MaxHp, int TailStart) ReadHealth(ReadOnlySpan<byte> afterNpcId)
    {
        if (afterNpcId.IsEmpty)
        {
            return (null, null, 0);
        }

        var offset = BytesBeforeHealth + ((afterNpcId[0] & ExtraBlockFlag) != 0 ? ExtraBlockBytes : 0);
        if (!TryReadVarUInt(afterNpcId, ref offset, out var hp) || !TryReadVarUInt(afterNpcId, ref offset, out var maxHp))
        {
            return (null, null, afterNpcId.Length);
        }

        return maxHp > 0 && hp <= maxHp ? ((long)hp, (long)maxHp, offset) : (null, null, offset);
    }

    /// <summary>
    /// El id de entidad del dueño en la cola de una invocación de un bloque:
    /// <code>... | 80 75 d5 2a bb 03 00 00 | varint e2 | u8 k1 | u8 02 | 3×f32 | 07 02 06 | u32 dueño | ...</code>
    /// Solo con esa forma exacta y la marca una sola vez: en otras formas el mismo campo apunta a bosses o a otras
    /// invocaciones (dos bloques, <c>0x1C</c>), y con <c>07 02 01</c> no se sabe qué es. Se ancla en la estructura y no en
    /// un offset, porque <c>e2</c> ocupa 2 o 3 bytes. En las seis capturas de referencia acierta 100 de 100 con dueño
    /// conocido. <c>null</c> si no tiene la forma o el u32 es 0.
    /// </summary>
    private static ulong? ReadOwner(ReadOnlySpan<byte> tail)
    {
        var mark = tail.IndexOf(OwnerBlockMark);
        if (mark < 0)
        {
            return null;
        }

        var offset = mark + OwnerBlockMark.Length;
        if (tail[offset..].IndexOf(OwnerBlockMark) >= 0 || !TryReadVarUInt(tail, ref offset, out _))
        {
            return null;
        }

        // k1 no se usa: después vienen el 02 fijo, la posición y 07 02 06.
        offset++;
        if (offset + BytesBeforeOwner + sizeof(uint) > tail.Length || tail[offset] != 0x02)
        {
            return null;
        }

        var prefix = offset + BytesBeforeOwner - 3;
        if (tail[prefix] != 0x07 || tail[prefix + 1] != 0x02 || tail[prefix + 2] != 0x06)
        {
            return null;
        }

        var owner = BinaryPrimitives.ReadUInt32LittleEndian(tail[(offset + BytesBeforeOwner)..]);
        return owner != 0 ? owner : null;
    }

    /// <summary>Lee un varint de hasta <see cref="VarInt.MaxBytes"/> bytes: un cuerpo corto o raro da <c>false</c>, no una excepción.</summary>
    private static bool TryReadVarUInt(ReadOnlySpan<byte> data, ref int offset, out ulong value)
    {
        value = 0;
        if (offset >= data.Length || !VarInt.TryRead(data[offset..Math.Min(data.Length, offset + VarInt.MaxBytes)], out value, out var read))
        {
            return false;
        }

        offset += read;
        return true;
    }
}
