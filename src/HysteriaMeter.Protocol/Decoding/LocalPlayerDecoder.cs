using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Paquete que el servidor manda solo sobre el jugador local (opcode 0x364A, lista de stats o buffs).
/// El primer varint es la entidad del local: en tres capturas nunca trajo a otro jugador ni a un NPC.
/// Llega en el primer bundle de cada conexión y cada pocos segundos después, así que también sirve
/// para detectar el cambio de entidad al cambiar de zona.
/// </summary>
public sealed class LocalPlayerDecoder : IPacketDecoder
{
    public PacketKind Kind => PacketKind.LocalPlayer;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        output.Add(new LocalPlayerEvent(timestamp, reader.ReadVarUInt(), Name: null));
    }
}

/// <summary>
/// Datos del jugador local al entrar a una zona (opcode 0x3633, ~2 KB), con su nombre:
/// <code>varint entity | 4 bytes | u8 | string name | u16 servidor | u32 ? | u8 | u32 nivel | u32 item level | ...</code>
/// Llega unos segundos después de abrir la conexión.
/// </summary>
public sealed class LocalPlayerInfoDecoder : IPacketDecoder
{
    public PacketKind Kind => PacketKind.LocalPlayerInfo;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var entity = reader.ReadVarUInt();
        reader.Skip(5);
        var name = reader.ReadUtf8String();
        if (PlayerNames.IsPlausible(name))
        {
            output.Add(new LocalPlayerEvent(timestamp, entity, name));
            if (reader.Remaining >= 15)
            {
                reader.Skip(7);
                var level = reader.ReadUInt32();
                var itemLevel = reader.ReadUInt32();
                if (level is >= 1 and <= 99 && itemLevel is > 0 and < PlayerGear.MaxItemLevel)
                {
                    output.Add(new PlayerGearEvent(timestamp, entity, (int)itemLevel, CombatPower: null));
                }
            }
        }
    }
}

/// <summary>Power (CP) del jugador local (opcode 0x3656, 16 bytes): <code>u64 actual | u64 ?</code></summary>
public sealed class LocalCombatPowerDecoder : IPacketDecoder
{
    public PacketKind Kind => PacketKind.LocalCombatPower;

    public void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output)
    {
        var reader = new PacketReader(body);
        var power = reader.ReadUInt64();
        if (power is > 0 and < PlayerGear.MaxCombatPower)
        {
            output.Add(new LocalCombatPowerEvent(timestamp, (long)power));
        }
    }
}
