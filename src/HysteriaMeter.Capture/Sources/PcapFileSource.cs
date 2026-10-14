using System.Runtime.CompilerServices;
using SharpPcap;
using SharpPcap.LibPcap;

namespace HysteriaMeter.Capture.Sources;

/// <summary>Reproduce un .pcap grabado. Es la base del desarrollo sin tener que estar jugando.</summary>
public sealed class PcapFileSource(string path, double speed = 0) : IPacketSource
{
    /// <remarks>Con <c>speed &gt; 0</c> respeta los tiempos originales (1 = tiempo real); con 0 lee lo más rápido posible.</remarks>
    public async IAsyncEnumerable<TcpSegment> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var device = new CaptureFileReaderDevice(path);
        device.Open(new DeviceConfiguration());

        DateTimeOffset? first = null;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!cancellationToken.IsCancellationRequested && device.GetNextPacket(out var capture) == GetPacketStatus.PacketRead)
        {
            if (!SegmentParser.TryParse(capture.GetPacket(), out var segment))
            {
                continue;
            }

            if (speed > 0)
            {
                first ??= segment.Timestamp;
                var due = (segment.Timestamp - first.Value) / speed - clock.Elapsed;
                if (due > TimeSpan.Zero)
                {
                    await Task.Delay(due, cancellationToken).ConfigureAwait(false);
                }
            }

            yield return segment;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
