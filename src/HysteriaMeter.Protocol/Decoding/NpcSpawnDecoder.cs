using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Aparición de un NPC, monstruo o invocación (opcode 0x3641):
/// <code>varint entityId | u8 tipo | u8 | u8 hasOwnerName | [string ownerName] | u32 npcId | u8 | u8 | pos 3×f32 | ... | varint HP | varint HPmax</code>
/// Las invocaciones de jugadores (tipo 0x5F) a veces traen el nombre del dueño; con eso el motor
/// les asigna el daño. Los monstruos normales traen <c>hasOwnerName = 0</c>.
/// </summary>
public sealed class NpcSpawnDecoder : IPacketDecoder
{
    public PacketKind Kind => PacketKind.EntitySpawn;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var entity = reader.ReadVarUInt();
        reader.Skip(2);
        var ownerName = reader.ReadByte() != 0 ? reader.ReadUtf8String() : null;
        var npcId = reader.ReadUInt32();
        var (hp, maxHp) = ReadTrailingHealth(body);
        output.Add(new EntitySpawnEvent(
            timestamp, entity, OwnerId: null, NpcId: npcId,
            OwnerName: ownerName is not null && PlayerNames.IsPlausible(ownerName) ? ownerName : null,
            Hp: hp, MaxHp: maxHp));
    }

    /// <summary>
    /// HP y HP máximo son los dos últimos varints del paquete. Se leen desde el final porque los campos
    /// entre la posición y la vida todavía no están claros. Si no cierran (HP mayor al máximo), se ignoran.
    /// </summary>
    internal static (long? Hp, long? MaxHp) ReadTrailingHealth(ReadOnlySpan<byte> body)
    {
        if (!TryReadLastVarUInt(body, out var maxHp, out var maxStart)
            || !TryReadLastVarUInt(body[..maxStart], out var hp, out _))
        {
            return (null, null);
        }

        return maxHp > 0 && hp <= maxHp ? ((long)hp, (long)maxHp) : (null, null);
    }

    /// <summary>
    /// Lee el varint que termina en el último byte: ese byte tiene el bit alto en 0 y los anteriores del
    /// mismo varint lo tienen en 1.
    /// </summary>
    private static bool TryReadLastVarUInt(ReadOnlySpan<byte> data, out ulong value, out int start)
    {
        value = 0;
        start = data.Length - 1;
        if (start < 0 || (data[start] & 0x80) != 0)
        {
            return false;
        }

        while (start > 0 && (data[start - 1] & 0x80) != 0 && data.Length - start < 10)
        {
            start--;
        }

        for (var i = data.Length - 1; i >= start; i--)
        {
            value = (value << 7) | (byte)(data[i] & 0x7F);
        }

        return true;
    }
}
