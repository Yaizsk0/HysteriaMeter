using HysteriaMeter.Protocol.Decoding;
using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;
using static HysteriaMeter.Protocol.Tests.FrameBuilder;

namespace HysteriaMeter.Protocol.Tests;

public class GameStreamDecoderTests
{
    private const ushort DamageOpcode = 0x0100;
    private const ushort BundleOpcode = 0x0200;

    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 21, 0, 0, TimeSpan.Zero);

    private static GameStreamDecoder CreateDecoder() => new(
        new OpcodeTable(new Dictionary<PacketKind, IReadOnlyList<ushort>>
        {
            [PacketKind.Damage] = [DamageOpcode],
            [PacketKind.Bundle] = [BundleOpcode],
        }),
        [new DamagePacketDecoder()]);

    private static byte[] DamageFrame(ulong source, ulong target, ulong skill, ulong amount, HitModifiers modifiers = HitModifiers.None) =>
        Frame(DamageOpcode, Concat(VarInts(source, target, skill, amount), [(byte)modifiers]));

    [Fact]
    public void Decodes_damage_frames()
    {
        var events = CreateDecoder().Push(DamageFrame(1, 99, 500, 1234, HitModifiers.Critical), T0);

        var damage = Assert.IsType<DamageEvent>(Assert.Single(events));
        Assert.Equal((1UL, 99UL, 500U, 1234L, HitModifiers.Critical), (damage.SourceId, damage.TargetId, damage.SkillId, damage.Amount, damage.Modifiers));
        Assert.Equal(T0, damage.Timestamp);
    }

    [Fact]
    public void Expands_lz4_bundles()
    {
        var inner = Concat(DamageFrame(1, 99, 500, 10), DamageFrame(2, 99, 501, 20));
        var bundle = Frame(BundleOpcode, BundleDecoder.Compress(inner));

        var events = CreateDecoder().Push(bundle, T0);

        Assert.Equal([10L, 20L], events.Cast<DamageEvent>().Select(e => e.Amount));
    }

    [Fact]
    public void Malformed_packet_is_counted_not_thrown()
    {
        var decoder = CreateDecoder();
        var events = decoder.Push(Frame(DamageOpcode, 0x01), T0);

        Assert.Empty(events);
        Assert.Equal(1, decoder.Stats.Errors);
    }

    [Fact]
    public void Unknown_opcodes_are_counted()
    {
        var decoder = CreateDecoder();
        decoder.Push(Concat(Frame(0x7777, 1), Frame(0x7777, 2)), T0);

        Assert.Equal(2, decoder.Stats.UnknownFrames);
        Assert.Equal(2, decoder.Stats.ByOpcode[0x7777]);
    }
}
