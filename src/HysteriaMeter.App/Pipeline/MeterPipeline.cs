using System.IO;
using System.Diagnostics;
using HysteriaMeter.Capture;
using HysteriaMeter.Capture.Filtering;
using HysteriaMeter.Capture.Reassembly;
using HysteriaMeter.Capture.Sources;
using HysteriaMeter.Combat;
using HysteriaMeter.Protocol.Decoding;
using HysteriaMeter.Protocol.Opcodes;
using HysteriaMeter.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HysteriaMeter.App.Pipeline;

/// <summary>
/// Captura → reensamblado TCP → decodificación → motor de combate. Corre en segundo plano;
/// la UI solo lee snapshots del <see cref="CombatEngine"/>.
/// </summary>
public sealed partial class MeterPipeline(
    CombatEngine engine,
    EncounterRepository repository,
    CaptureRecorder recorder,
    CaptureStatus status,
    DecoderStats stats,
    IOptions<CaptureSettings> captureOptions,
    ILogger<MeterPipeline> logger) : BackgroundService
{
    private readonly CaptureSettings _settings = captureOptions.Value;
    private readonly Stopwatch _sinceLastSegment = Stopwatch.StartNew();
    private DateTimeOffset _lastSegmentAt = DateTimeOffset.UtcNow;

    /// <summary>La captura en vivo, para que el tick publique sus descartes; <c>null</c> repitiendo un .pcap.</summary>
    private LiveCaptureSource? _live;

    public DecoderStats Stats => stats;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await repository.InitializeAsync(stoppingToken).ConfigureAwait(false);
        engine.EncounterFinished += OnEncounterFinished;

        var opcodes = LoadOpcodes();
        var decoders = PacketDecoders.All;

        using var ticker = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var tickTask = TickLoopAsync(ticker.Token);

        var source = CreateSource();
        Action<string?>? forwardRecording = null;
        if (source is LiveCaptureSource live)
        {
            Volatile.Write(ref _live, live);
            forwardRecording = live.SetRecordPath;
            recorder.Changed += forwardRecording;
            if (recorder.CurrentPath is { } path)
            {
                live.SetRecordPath(path);
            }
        }

        await using (source.ConfigureAwait(false))
        {
            var flows = new TcpFlowTracker();
            var streams = new Dictionary<FlowKey, GameStreamDecoder>();

            try
            {
                await foreach (var segment in source.ReadAllAsync(stoppingToken).ConfigureAwait(false))
                {
                    _lastSegmentAt = segment.Timestamp;
                    _sinceLastSegment.Restart();
                    status.MarkTraffic(DateTime.UtcNow);

                    var result = flows.Push(segment);
                    if (!segment.Flow.IsFromServer() || (result.Chunks.Count == 0 && !result.GapDetected))
                    {
                        continue;
                    }

                    if (!streams.TryGetValue(segment.Flow, out var stream))
                    {
                        stream = new GameStreamDecoder(opcodes, decoders, stats: Stats);
                        streams[segment.Flow] = stream;
                        LogNewFlow(segment.Flow);
                    }

                    if (result.GapDetected)
                    {
                        stream.Resync();
                    }

                    foreach (var chunk in result.Chunks)
                    {
                        foreach (var e in stream.Push(chunk.Span, segment.Timestamp))
                        {
                            engine.Apply(e);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                status.Fail(ex.Message);
                LogCaptureFailed(ex);
            }
        }

        if (forwardRecording is not null)
        {
            recorder.Changed -= forwardRecording;
        }

        await ticker.CancelAsync().ConfigureAwait(false);
        await tickTask.ConfigureAwait(false);
    }

    private IPacketSource CreateSource()
    {
        if (_settings.Mode == CaptureMode.Replay)
        {
            var path = _settings.ReplayPath ?? throw new InvalidOperationException("Capture:ReplayPath es obligatorio en modo Replay.");
            return new PcapFileSource(path, _settings.ReplaySpeed);
        }

        IConnectionFilter filter = _settings.ServerPorts.Count > 0
            ? new ServerPortFilter(_settings.ServerPorts)
            : new ProcessConnectionFilter(_settings.ProcessNames);

        return new LiveCaptureSource(
            new LiveCaptureOptions
            {
                Device = _settings.Device,
                RecordPath = string.IsNullOrWhiteSpace(_settings.RecordPath) ? null : _settings.RecordPath,
            },
            filter);
    }

    private OpcodeTable LoadOpcodes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, _settings.OpcodesPath);
        if (!File.Exists(path))
        {
            LogOpcodesMissing(path);
            return OpcodeTable.Empty;
        }

        var table = OpcodeTable.Load(path);
        LogOpcodesLoaded(table.Count, path);
        return table;
    }

    /// <summary>
    /// Cierra encuentros por inactividad usando el reloj de la captura (sirve también en replay) y publica los
    /// descartes de la cola de la captura en vivo para Diagnóstico.
    /// </summary>
    private async Task TickLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                engine.Tick(_lastSegmentAt + _sinceLastSegment.Elapsed);
                if (Volatile.Read(ref _live) is { } live)
                {
                    status.SetDropped(live.Dropped);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void OnEncounterFinished(EncounterSnapshot snapshot)
    {
        try
        {
            await repository.SaveAsync(snapshot).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogSaveFailed(ex, snapshot.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Nueva conexión del juego: {Flow}")]
    private partial void LogNewFlow(FlowKey flow);

    [LoggerMessage(Level = LogLevel.Error, Message = "La captura se detuvo")]
    private partial void LogCaptureFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se encontró {Path}; no se va a decodificar ningún paquete")]
    private partial void LogOpcodesMissing(string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} opcodes cargados de {Path}")]
    private partial void LogOpcodesLoaded(int count, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "No se pudo guardar el encuentro {Id}")]
    private partial void LogSaveFailed(Exception ex, Guid id);
}
