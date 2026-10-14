using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>Convierte el cuerpo de un tipo de paquete en eventos. Un decoder por <see cref="PacketKind"/>.</summary>
public interface IPacketDecoder
{
    PacketKind Kind { get; }

    void Decode(ReadOnlySpan<byte> body, DateTimeOffset timestamp, ICollection<CombatEvent> output);
}
