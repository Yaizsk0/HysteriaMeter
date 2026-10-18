using System.Net;
using HysteriaMeter.Capture.Filtering;
using HysteriaMeter.Capture.Sources;

namespace HysteriaMeter.Capture.Tests;

/// <summary>La cola de la captura en vivo, sin abrir placas (Npcap no hace falta).</summary>
public class LiveCaptureSourceTests
{
    private static readonly TcpSegment Segment = new(
        DateTimeOffset.UnixEpoch, new IPEndPoint(IPAddress.Parse("203.0.113.10"), 7777), new IPEndPoint(IPAddress.Parse("192.168.1.20"), 50000),
        100, Syn: false, Fin: false, Rst: false, new byte[] { 1 });

    [Fact]
    public void Full_queue_counts_every_dropped_segment()
    {
        // Con la cola llena, DropWrite descarta el segmento nuevo pero TryWrite devuelve true: el conteo no puede
        // depender de ese resultado.
        var source = new LiveCaptureSource(new LiveCaptureOptions { QueueCapacity = 1 }, new AcceptAllFilter());
        var queue = source.CreateQueue();

        for (var i = 0; i < 3; i++)
        {
            Assert.True(queue.Writer.TryWrite(Segment));
        }

        Assert.Equal(2, source.Dropped);
    }
}
