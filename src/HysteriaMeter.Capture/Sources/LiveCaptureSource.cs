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
public sealed class LiveCaptureSource(LiveCaptureOptions options, IConnectionFilter filter) : IPacketSource
{
    private ILiveDevice? _device;
    private CaptureFileWriterDevice? _writer;
    private long _dropped;

    /// <summary>Segmentos descartados porque el consumidor no daba abasto.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    public static IReadOnlyList<(string Name, string Description)> ListDevices() =>
        [.. CaptureDeviceList.Instance.Select(d => (d.Name, d.Description ?? string.Empty))];

    public async IAsyncEnumerable<TcpSegment> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<TcpSegment>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

        _device = SelectDevice();
        _device.Open(new DeviceConfiguration { Mode = DeviceModes.None, ReadTimeout = 100 });
        _device.Filter = options.BpfFilter;

        if (options.RecordPath is { } recordPath)
        {
            _writer = new CaptureFileWriterDevice(recordPath, FileMode.Create);
            _writer.Open(new DeviceConfiguration { LinkLayerType = _device.LinkType });
        }

        _device.OnPacketArrival += (_, e) =>
        {
            var raw = e.GetPacket();
            if (!SegmentParser.TryParse(raw, out var segment) || !filter.IsGameTraffic(segment))
            {
                return;
            }

            _writer?.Write(raw);
            if (!channel.Writer.TryWrite(segment))
            {
                Interlocked.Increment(ref _dropped);
            }
        };
        _device.StartCapture();

        await using var registration = cancellationToken.Register(() => channel.Writer.TryComplete());
        await foreach (var segment in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            yield return segment;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_device is { Started: true })
        {
            _device.StopCapture();
        }

        _device?.Dispose();
        _writer?.Dispose();
        return ValueTask.CompletedTask;
    }

    private ILiveDevice SelectDevice()
    {
        var devices = CaptureDeviceList.Instance;
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("No se encontraron placas de red. ¿Está instalado Npcap (modo compatible WinPcap)?");
        }

        if (string.IsNullOrWhiteSpace(options.Device))
        {
            // Sin preferencia: la primera que no sea loopback. Se puede fijar en la configuración.
            return devices.FirstOrDefault(d => !(d.Description ?? d.Name).Contains("loopback", StringComparison.OrdinalIgnoreCase)) ?? devices[0];
        }

        return devices.FirstOrDefault(d =>
                   d.Name.Contains(options.Device, StringComparison.OrdinalIgnoreCase) ||
                   (d.Description ?? string.Empty).Contains(options.Device, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"No existe la placa '{options.Device}'.");
    }
}
