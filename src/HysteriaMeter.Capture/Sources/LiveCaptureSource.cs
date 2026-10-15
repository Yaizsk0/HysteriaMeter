using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HysteriaMeter.Capture.Filtering;
using SharpPcap;
using SharpPcap.LibPcap;

namespace HysteriaMeter.Capture.Sources;

/// <summary>
/// Captura pasiva con Npcap. Solo lee copias de los paquetes que pasan por la placa:
/// no envía, no modifica y no toca el proceso del juego.
/// </summary>
/// <remarks>
/// Si no se indica placa, escucha en todas (salvo loopback) y se queda con la primera por la que
/// aparece tráfico del juego: con Wi-Fi, VPN o adaptadores virtuales la "primera" placa casi nunca es la correcta.
/// </remarks>
public sealed class LiveCaptureSource(LiveCaptureOptions options, IConnectionFilter filter) : IPacketSource
{
    private readonly List<ILiveDevice> _devices = [];
    private readonly Lock _selectionGate = new();
    private ILiveDevice? _selected;
    private CaptureFileWriterDevice? _writer;
    private long _dropped;

    /// <summary>Segmentos descartados porque el consumidor no daba abasto.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Placa elegida (cuando ya apareció tráfico del juego).</summary>
    public string? SelectedDevice => _selected?.Description ?? _selected?.Name;

    public static IReadOnlyList<(string Name, string Description)> ListDevices() =>
        [.. CaptureDeviceList.Instance.Select(d => (d.Name, d.Description ?? string.Empty))];

    public async IAsyncEnumerable<TcpSegment> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<TcpSegment>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

        _devices.AddRange(CandidateDevices());
        foreach (var device in _devices)
        {
            device.Open(new DeviceConfiguration { Mode = DeviceModes.None, ReadTimeout = 100 });
            device.Filter = options.BpfFilter;
            device.OnPacketArrival += (_, e) => OnPacket(device, e, channel.Writer);
            device.StartCapture();
        }

        await using var registration = cancellationToken.Register(() => channel.Writer.TryComplete());
        await foreach (var segment in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            yield return segment;
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var device in _devices)
        {
            Stop(device);
        }

        _writer?.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnPacket(ILiveDevice device, PacketCapture e, ChannelWriter<TcpSegment> writer)
    {
        var raw = e.GetPacket();
        if (!SegmentParser.TryParse(raw, out var segment) || !filter.IsGameTraffic(segment) || !Select(device))
        {
            return;
        }

        _writer?.Write(raw);
        if (!writer.TryWrite(segment))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>La primera placa con tráfico del juego queda elegida; las demás se detienen.</summary>
    private bool Select(ILiveDevice device)
    {
        if (_selected is not null)
        {
            return ReferenceEquals(_selected, device);
        }

        lock (_selectionGate)
        {
            if (_selected is not null)
            {
                return ReferenceEquals(_selected, device);
            }

            if (options.RecordPath is { } recordPath)
            {
                _writer = new CaptureFileWriterDevice(recordPath, FileMode.Create);
                _writer.Open(new DeviceConfiguration { LinkLayerType = device.LinkType });
            }

            _selected = device;
        }

        // Detener las otras placas fuera del callback de captura.
        _ = Task.Run(() =>
        {
            foreach (var other in _devices.Where(d => !ReferenceEquals(d, device)))
            {
                Stop(other);
            }
        });
        return true;
    }

    private IEnumerable<ILiveDevice> CandidateDevices()
    {
        var devices = CaptureDeviceList.Instance;
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("No se encontraron placas de red. ¿Está instalado Npcap (modo compatible WinPcap)?");
        }

        if (!string.IsNullOrWhiteSpace(options.Device))
        {
            return [devices.FirstOrDefault(d =>
                       d.Name.Contains(options.Device, StringComparison.OrdinalIgnoreCase) ||
                       (d.Description ?? string.Empty).Contains(options.Device, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"No existe la placa '{options.Device}'.")];
        }

        return devices.Where(d => !(d.Description ?? d.Name).Contains("loopback", StringComparison.OrdinalIgnoreCase));
    }

    private static void Stop(ILiveDevice device)
    {
        try
        {
            if (device.Started)
            {
                device.StopCapture();
            }

            device.Close();
        }
        catch (PcapException)
        {
            // Una placa que no se pudo detener no debe impedir cerrar las demás.
        }
    }
}
