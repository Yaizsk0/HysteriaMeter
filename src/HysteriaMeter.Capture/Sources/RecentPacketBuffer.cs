using SharpPcap;
using SharpPcap.LibPcap;

namespace HysteriaMeter.Capture.Sources;

/// <summary>
/// Grabación retroactiva: los últimos minutos del tráfico del juego en memoria, para volcarlos a un .pcap cuando ya
/// pasó algo (una pelea que no cierra). Guarda los mismos paquetes que grabaría ● REC, sin enviar ni cambiar nada.
/// </summary>
/// <param name="window">Cuánto hacia atrás, medido con el reloj de la captura (el del paquete más nuevo).</param>
/// <param name="maxBytes">Tope de memoria por si una zona muy llena manda mucho más tráfico que lo normal.</param>
public sealed class RecentPacketBuffer(TimeSpan window, long maxBytes)
{
    private readonly Queue<RawCapture> _packets = new();
    private readonly Lock _gate = new();
    private long _bytes;

    /// <summary>Bytes de los paquetes guardados (sin las cabeceras del .pcap).</summary>
    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>Agrega una copia ya hecha del paquete (la de <see cref="PacketCapture.GetPacket"/>) y descarta lo viejo.</summary>
    public void Add(RawCapture packet)
    {
        lock (_gate)
        {
            _packets.Enqueue(packet);
            _bytes += packet.Data.Length;

            var oldest = packet.Timeval.Date - window;
            while (_packets.Count > 1 && (_bytes > maxBytes || _packets.Peek().Timeval.Date < oldest))
            {
                _bytes -= _packets.Dequeue().Data.Length;
            }
        }
    }

    /// <summary>Copia de lo guardado, del más viejo al más nuevo: se escribe fuera del lock mientras la captura sigue.</summary>
    public IReadOnlyList<RawCapture> Snapshot()
    {
        lock (_gate)
        {
            return [.. _packets];
        }
    }

    /// <summary>
    /// Escribe lo guardado en <paramref name="path"/> con el mismo escritor que ● REC, así "hysteria-rec dump" lo lee igual.
    /// <c>null</c> sin paquetes: no crea el archivo. Si no se puede escribir tira <see cref="IOException"/> (también
    /// cuando falla libpcap), así quien guarda no depende de SharpPcap.
    /// </summary>
    public SavedPackets? SaveTo(string path)
    {
        var packets = Snapshot();
        if (packets.Count == 0)
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            using var writer = new CaptureFileWriterDevice(path, FileMode.Create);
            writer.Open(new DeviceConfiguration { LinkLayerType = packets[0].LinkLayerType });
            foreach (var packet in packets)
            {
                writer.Write(packet);
            }
        }
        catch (PcapException ex)
        {
            throw new IOException(ex.Message, ex);
        }

        return new SavedPackets(path, packets.Count, packets[^1].Timeval.Date - packets[0].Timeval.Date);
    }
}

/// <summary>Un volcado de <see cref="RecentPacketBuffer"/>: el archivo, cuántos paquetes y cuánto tiempo cubre.</summary>
public sealed record SavedPackets(string Path, int Packets, TimeSpan Span);
