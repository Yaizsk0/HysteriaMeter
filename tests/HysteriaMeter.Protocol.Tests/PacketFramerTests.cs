using HysteriaMeter.Protocol.Framing;
using static HysteriaMeter.Protocol.Tests.FrameBuilder;

namespace HysteriaMeter.Protocol.Tests;

public class PacketFramerTests
{
    [Fact]
    public void Splits_real_stream_into_frames()
    {
        var frames = new PacketFramer().Push(Hex(RealCapture.ThreeFrames));

        Assert.Equal([0x371D, 0x371D, 0x3600], frames.Select(f => f.Opcode));
        Assert.Equal([11, 11, 8], frames.Select(f => f.Body.Length));
    }

    [Fact]
    public void Splits_concatenated_frames()
    {
        var framer = new PacketFramer();
        var frames = framer.Push(Concat(Frame(0x3804, 1, 2, 3), Frame(0x0001)));

        Assert.Collection(
            frames,
            f => { Assert.Equal(0x3804, f.Opcode); Assert.Equal([1, 2, 3], f.Body.ToArray()); },
            f => { Assert.Equal(0x0001, f.Opcode); Assert.True(f.Body.IsEmpty); });
    }

    [Fact]
    public void Waits_for_frames_split_across_segments()
    {
        var framer = new PacketFramer();
        var data = Frame(0x3804, Enumerable.Range(0, 200).Select(i => (byte)i).ToArray());

        Assert.Empty(framer.Push(data.AsSpan(0, 1)));
        Assert.Empty(framer.Push(data.AsSpan(1, 100)));
        var frames = framer.Push(data.AsSpan(101));

        var frame = Assert.Single(frames);
        Assert.Equal(200, frame.Body.Length);
    }

    [Fact]
    public void Absurd_length_counts_a_desync_and_drops_buffer()
    {
        var framer = new PacketFramer(new ProtocolOptions { MaxFrameSize = 16 });

        Assert.Empty(framer.Push(VarInts(1000)));
        Assert.Equal(1, framer.Desyncs);
        Assert.Single(framer.Push(Frame(0x0002, 9)));
    }

    [Fact]
    public void Garbage_with_overlong_varint_is_a_desync_not_an_exception()
    {
        var framer = new PacketFramer();

        Assert.Empty(framer.Push(Enumerable.Repeat((byte)0xFF, 12).ToArray()));
        Assert.Equal(1, framer.Desyncs);
    }
}
