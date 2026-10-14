using System.Net;
using HysteriaMeter.Capture.Reassembly;

namespace HysteriaMeter.Capture.Tests;

public class TcpStreamReassemblerTests
{
    private static readonly IPEndPoint Server = new(IPAddress.Parse("203.0.113.10"), 7777);
    private static readonly IPEndPoint Client = new(IPAddress.Parse("192.168.1.20"), 50000);

    private static TcpSegment Segment(uint seq, params byte[] payload) =>
        new(DateTimeOffset.UnixEpoch, Server, Client, seq, Syn: false, Fin: false, Rst: false, payload);

    private static byte[] Flatten(ReassemblyResult result) => result.Chunks.SelectMany(c => c.ToArray()).ToArray();

    [Fact]
    public void In_order_segments_pass_through()
    {
        var r = new TcpStreamReassembler();
        Assert.Equal([1, 2], Flatten(r.Push(Segment(100, 1, 2))));
        Assert.Equal([3], Flatten(r.Push(Segment(102, 3))));
    }

    [Fact]
    public void Out_of_order_segments_are_reordered()
    {
        var r = new TcpStreamReassembler();
        r.Push(Segment(100, 1));
        Assert.Empty(Flatten(r.Push(Segment(103, 4))));
        Assert.Empty(Flatten(r.Push(Segment(102, 3))));
        Assert.Equal([2, 3, 4], Flatten(r.Push(Segment(101, 2))));
    }

    [Fact]
    public void Retransmissions_and_overlaps_are_trimmed()
    {
        var r = new TcpStreamReassembler();
        r.Push(Segment(100, 1, 2, 3));
        Assert.Empty(Flatten(r.Push(Segment(100, 1, 2, 3))));
        Assert.Equal([4, 5], Flatten(r.Push(Segment(102, 3, 4, 5))));
    }

    [Fact]
    public void Handles_sequence_wraparound()
    {
        var r = new TcpStreamReassembler();
        r.Push(Segment(uint.MaxValue - 1, 1, 2));
        Assert.Equal([3, 4], Flatten(r.Push(Segment(0, 3, 4))));
    }

    [Fact]
    public void Unfillable_gap_is_skipped_and_reported()
    {
        var r = new TcpStreamReassembler(maxPendingBytes: 4);
        r.Push(Segment(100, 1));
        Assert.False(r.Push(Segment(110, 1, 2, 3)).GapDetected);

        var result = r.Push(Segment(113, 4, 5));
        Assert.True(result.GapDetected);
        Assert.Equal([1, 2, 3, 4, 5], Flatten(result));
        Assert.Equal(1, r.Gaps);
    }

    [Fact]
    public void Syn_resets_the_stream()
    {
        var r = new TcpStreamReassembler();
        r.Push(Segment(100, 1));
        r.Push(Segment(5000, []) with { Syn = true });
        Assert.Equal([9], Flatten(r.Push(Segment(5001, 9))));
    }
}
