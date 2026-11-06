using System.Net;
using System.Net.NetworkInformation;
using HysteriaMeter.Capture.Sources;
using PacketDotNet;
using SharpPcap;

namespace HysteriaMeter.Capture.Tests;

/// <summary>La grabación retroactiva: los últimos minutos del tráfico del juego en memoria, para volcarlos a un .pcap.</summary>
public sealed class RecentPacketBufferTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 30, 22, 0, 0, DateTimeKind.Utc);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hysteria-recent-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Keeps_only_the_window_measured_with_the_capture_clock()
    {
        var buffer = new RecentPacketBuffer(TimeSpan.FromMinutes(10), maxBytes: 1_000_000);

        buffer.Add(Raw(Start));
        buffer.Add(Raw(Start.AddMinutes(5)));
        buffer.Add(Raw(Start.AddMinutes(11)));

        Assert.Equal([Start.AddMinutes(5), Start.AddMinutes(11)], buffer.Snapshot().Select(p => p.Timeval.Date));
    }

    [Fact]
    public void Drops_the_oldest_packets_past_the_byte_cap()
    {
        // Una zona muy llena puede mandar mucho más que lo normal: el tope de bytes acota la memoria igual.
        var buffer = new RecentPacketBuffer(TimeSpan.FromMinutes(10), maxBytes: 250);

        for (var i = 0; i < 3; i++)
        {
            buffer.Add(Raw(Start.AddSeconds(i), size: 100));
        }

        Assert.Equal([Start.AddSeconds(1), Start.AddSeconds(2)], buffer.Snapshot().Select(p => p.Timeval.Date));
        Assert.Equal(200, buffer.Bytes);
    }

    [Fact]
    public void Snapshot_does_not_change_with_later_packets()
    {
        // El volcado escribe la copia fuera del lock mientras la captura sigue agregando.
        var buffer = new RecentPacketBuffer(TimeSpan.FromMinutes(10), maxBytes: 1_000_000);
        buffer.Add(Raw(Start));

        var snapshot = buffer.Snapshot();
        buffer.Add(Raw(Start.AddSeconds(1)));

        Assert.Single(snapshot);
    }

    [Fact]
    public void Empty_buffer_saves_no_file()
    {
        var buffer = new RecentPacketBuffer(TimeSpan.FromMinutes(10), maxBytes: 1_000_000);
        var path = Path.Combine(_folder, "vacio.pcap");

        Assert.Null(buffer.SaveTo(path));
        Assert.False(File.Exists(path));
    }

    [NpcapFact]
    public async Task Saved_file_reads_back_like_a_REC_recording()
    {
        // El mismo escritor que ● REC y el mismo lector que "hysteria-rec dump": el archivo tiene que servir igual.
        var buffer = new RecentPacketBuffer(TimeSpan.FromMinutes(10), maxBytes: 1_000_000);
        buffer.Add(Frame(Start, sequence: 100, payload: [1, 2, 3]));
        buffer.Add(Frame(Start.AddMinutes(7), sequence: 103, payload: [4, 5]));
        var path = Path.Combine(_folder, "ultimo.pcap");

        var saved = buffer.SaveTo(path);

        Assert.NotNull(saved);
        Assert.Equal(path, saved.Path);
        Assert.Equal(2, saved.Packets);
        Assert.Equal(TimeSpan.FromMinutes(7), saved.Span);

        var segments = new List<TcpSegment>();
        await using (var source = new PcapFileSource(path))
        {
            await foreach (var segment in source.ReadAllAsync(CancellationToken.None))
            {
                segments.Add(segment);
            }
        }

        Assert.Equal([100u, 103u], segments.Select(s => s.Sequence));
        Assert.Equal([new DateTimeOffset(Start), new DateTimeOffset(Start.AddMinutes(7))], segments.Select(s => s.Timestamp));
        Assert.Equal([4, 5], segments[1].Payload.ToArray());
    }

    private static RawCapture Raw(DateTime at, int size = 10) => new(LinkLayers.Ethernet, new PosixTimeval(at), new byte[size]);

    /// <summary>Un segmento TCP del servidor del juego como lo entrega la placa (Ethernet → IPv4 → TCP).</summary>
    private static RawCapture Frame(DateTime at, uint sequence, byte[] payload)
    {
        var tcp = new TcpPacket(7777, 50000) { SequenceNumber = sequence, Acknowledgment = true, PayloadData = payload };
        var ip = new IPv4Packet(IPAddress.Parse("203.0.113.10"), IPAddress.Parse("192.168.1.20")) { PayloadPacket = tcp };
        var ethernet = new EthernetPacket(PhysicalAddress.Parse("00-11-22-33-44-55"), PhysicalAddress.Parse("66-77-88-99-AA-BB"), EthernetType.IPv4)
        {
            PayloadPacket = ip,
        };
        ip.UpdateCalculatedValues();
        return new RawCapture(LinkLayers.Ethernet, new PosixTimeval(at), ethernet.Bytes);
    }
}
