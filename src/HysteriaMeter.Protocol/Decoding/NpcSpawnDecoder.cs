using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Aparición de un NPC, monstruo o invocación (opcode 0x3641):
/// <code>varint entityId | u8 tipo | u8 | u8 hasOwnerName | [string ownerName] | u32 npcId | u8 flags | u8 | pos 3×f32 | f32 | [12 bytes si flags tiene 0x08] | 3 bytes | varint HP | varint HPmax | ...</code>
/// Las invocaciones de jugadores (tipos 0x5F, 0x1F, 0x1C y 0x2F) a veces traen el nombre del dueño; con eso el motor
/// les asigna el daño. Los monstruos normales traen <c>hasOwnerName = 0</c>. El decoder no mira el tipo. Las de un
/// bloque traen además el id del dueño en la cola, que todavía no se lee (T020 de la 002; ver "Dueño de la invocación"
/// en <c>protocol-notes.md</c>).
/// </summary>
public sealed class NpcSpawnDecoder : IPacketDecoder
{
    /// <summary>Bytes entre el npcId y la vida: dos u8, la posición, un f32 y tres bytes sin identificar.</summary>
    private const int BytesBeforeHealth = 2 + (4 * 4) + 3;

    /// <summary>
    /// Con este bit en el u8 que sigue al npcId vienen 12 bytes más, sin identificar, antes de la vida. En las capturas el
    /// u8 vale 0x00, 0x40 (no agrega nada) o 0x08, y el bit no depende del tipo.
    /// </summary>
    private const byte ExtraBlockFlag = 0x08;

    private const int ExtraBlockBytes = 12;

    public PacketKind Kind => PacketKind.EntitySpawn;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var entity = reader.ReadVarUInt();
        reader.Skip(2);
        var ownerName = reader.ReadByte() != 0 ? reader.ReadUtf8String() : null;
        var npcId = reader.ReadUInt32();
        var (hp, maxHp) = ReadHealth(body[reader.Position..]);
        output.Add(new EntitySpawnEvent(
            timestamp, entity, OwnerId: null, NpcId: npcId,
            OwnerName: ownerName is not null && PlayerNames.IsPlausible(ownerName) ? ownerName : null,
            Hp: hp, MaxHp: maxHp));
    }

    /// <summary>
    /// HP y HP máximo van en posición fija después del npcId, 12 bytes más adelante con <see cref="ExtraBlockFlag"/>.
    /// Después puede venir más (casi todos los cuerpos de la dungeon y del mini boss), así que no se leen desde el final:
    /// los dos últimos varints daban basura. Las invocaciones sin posición no los traen, y si no cierran (sin máximo, o HP
    /// mayor al máximo) se ignoran.
    /// </summary>
    private static (long? Hp, long? MaxHp) ReadHealth(ReadOnlySpan<byte> afterNpcId)
    {
        if (afterNpcId.IsEmpty)
        {
            return (null, null);
        }

        var offset = BytesBeforeHealth + ((afterNpcId[0] & ExtraBlockFlag) != 0 ? ExtraBlockBytes : 0);
        if (!TryReadVarUInt(afterNpcId, ref offset, out var hp) || !TryReadVarUInt(afterNpcId, ref offset, out var maxHp))
        {
            return (null, null);
        }

        return maxHp > 0 && hp <= maxHp ? ((long)hp, (long)maxHp) : (null, null);
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
