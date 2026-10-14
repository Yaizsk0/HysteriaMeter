namespace HysteriaMeter.Protocol.Decoding;

/// <summary>Decoders implementados. Un decoder nuevo se registra acá.</summary>
public static class PacketDecoders
{
    public static IReadOnlyList<IPacketDecoder> All { get; } =
    [
        new DamagePacketDecoder(),
        new PeriodicEffectDecoder(),
        new PlayerSpawnDecoder(),
        new NpcSpawnDecoder(),
        new KillPacketDecoder(),
    ];
}
