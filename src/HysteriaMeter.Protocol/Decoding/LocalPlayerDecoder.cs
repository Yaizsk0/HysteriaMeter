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
/// <code>varint entity | 4 bytes | u8 | string name | u16 ? | ...</code>
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
        }
    }
}
