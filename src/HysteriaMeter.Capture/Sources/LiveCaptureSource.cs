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
    private readonly Lock _writerGate = new();
    private ILiveDevice? _selected;
    private CaptureFileWriterDevice? _writer;
    private string? _recordPath = options.RecordPath;
    private readonly RecentPacketBuffer? _recent = options.RecentWindow > TimeSpan.Zero
        ? new RecentPacketBuffer(options.RecentWindow, options.RecentMaxBytes)
        : null;
    private long _dropped;

    /// <summary>
    /// Segmentos del juego que el meter descartó porque su cola estaba llena (el pipeline no daba abasto).
    /// No son pérdidas de la placa ni del driver de Npcap: esos paquetes ya llegaron y se tiraron acá.
    /// </summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Placa elegida (cuando ya apareció tráfico del juego).</summary>
    public string? SelectedDevice => _selected?.Description ?? _selected?.Name;

    /// <summary>
    /// Empieza a grabar en <paramref name="path"/> (o deja de grabar con <c>null</c>) sin cortar la captura.
    /// Si todavía no se eligió placa, el archivo se abre cuando aparece el primer paquete del juego.
    /// </summary>
    public void SetRecordPath(string? path)
    {
        lock (_writerGate)
        {
            CloseWriter();
            _recordPath = path;
            if (path is not null && _selected is { } device)
            {
                OpenWriter(device);
            }
        }
    }

    /// <summary>
    /// Vuelca a <paramref name="path"/> el tráfico del juego de los últimos <see cref="LiveCaptureOptions.RecentWindow"/>
    /// (grabación retroactiva). <c>null</c> si el buffer está apagado o todavía no llegó tráfico del juego.
    /// </summary>
    public SavedPackets? SaveRecent(string path) => _recent?.SaveTo(path);

    public static IReadOnlyList<(string Name, string Description)> ListDevices() =>
        [.. CaptureDeviceList.Instance.Select(d => (d.Name, d.Description ?? string.Empty))];

    public async IAsyncEnumerable<TcpSegment> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = CreateQueue();

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

        lock (_writerGate)
        {
            CloseWriter();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Cola entre el callback de captura y el pipeline. Llena, descarta el segmento nuevo; con DropWrite TryWrite
    /// devuelve true igual, así que el descarte se cuenta en el aviso del canal y no en el resultado de TryWrite.
    /// </summary>
    internal Channel<TcpSegment> CreateQueue() =>
        Channel.CreateBounded<TcpSegment>(
            new BoundedChannelOptions(options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
            },
            _ => Interlocked.Increment(ref _dropped));

    private void OnPacket(ILiveDevice device, PacketCapture e, ChannelWriter<TcpSegment> writer)
    {
        var raw = e.GetPacket();
        if (!SegmentParser.TryParse(raw, out var segment) || !filter.IsGameTraffic(segment) || !Select(device))
        {
            return;
        }

        lock (_writerGate)
        {
            _writer?.Write(raw);
        }

        _recent?.Add(raw);

        // Solo da false con la captura ya cerrada; los descartes por cola llena los cuenta CreateQueue.
        _ = writer.TryWrite(segment);
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

            _selected = device;
            lock (_writerGate)
            {
                if (_recordPath is not null)
                {
                    OpenWriter(device);
                }
            }
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

    private void OpenWriter(ILiveDevice device)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_recordPath!))!);
        _writer = new CaptureFileWriterDevice(_recordPath!, FileMode.Create);
        _writer.Open(new DeviceConfiguration { LinkLayerType = device.LinkType });
    }

    private void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
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
