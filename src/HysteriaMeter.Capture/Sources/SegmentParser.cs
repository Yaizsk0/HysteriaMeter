using System.Net;
using PacketDotNet;
using SharpPcap;

namespace HysteriaMeter.Capture.Sources;

internal static class SegmentParser
{
    public static bool TryParse(RawCapture raw, out TcpSegment segment)
    {
        segment = default;
        Packet packet;
        try
        {
            packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidOperationException)
        {
            return false;
        }

        if (packet.Extract<TcpPacket>() is not { ParentPacket: IPPacket ip } tcp)
        {
            return false;
        }

        segment = new TcpSegment(
            new DateTimeOffset(raw.Timeval.Date, TimeSpan.Zero),
            new IPEndPoint(ip.SourceAddress, tcp.SourcePort),
            new IPEndPoint(ip.DestinationAddress, tcp.DestinationPort),
            tcp.SequenceNumber,
            tcp.Synchronize,
            tcp.Finished,
            tcp.Reset,
            tcp.PayloadData ?? []);
        return true;
    }
}
