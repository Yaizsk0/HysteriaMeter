// hysteria-rec: herramienta de descubrimiento del protocolo.
//   hysteria-rec devices
//   hysteria-rec record <salida.pcap> [--device <nombre>] [--process AION2] [--port 1234]
//   hysteria-rec dump <entrada.pcap> [--opcodes data/opcodes.json] [--opcode 0x0638] [--samples 3]
//   hysteria-rec replay <entrada.pcap> [--opcodes data/opcodes.json]
using System.Globalization;
using HysteriaMeter.Combat;
using HysteriaMeter.Capture;
using HysteriaMeter.Capture.Filtering;
using HysteriaMeter.Capture.Reassembly;
using HysteriaMeter.Capture.Sources;
using HysteriaMeter.Protocol.Decoding;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

return args switch
{
    ["devices", ..] => Devices(),
    ["record", var output, .. var rest] => await RecordAsync(output, Options.Parse(rest)),
    ["dump", var input, .. var rest] => await DumpAsync(input, Options.Parse(rest)),
    ["replay", var input, .. var rest] => await ReplayAsync(input, Options.Parse(rest)),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("""
        hysteria-rec devices
        hysteria-rec record <salida.pcap> [--device <nombre>] [--process AION2] [--port <puerto>]
        hysteria-rec dump <entrada.pcap> [--opcodes data/opcodes.json] [--opcode 0x0638] [--samples 3]
        hysteria-rec replay <entrada.pcap> [--opcodes data/opcodes.json]
        """);
    return 1;
}

static int Devices()
{
    foreach (var (name, description) in LiveCaptureSource.ListDevices())
    {
        Console.WriteLine($"{description}\n    {name}");
    }

    return 0;
}

static async Task<int> RecordAsync(string output, Options options)
{
    IConnectionFilter filter = options.Get("port") is { } port
        ? new ServerPortFilter([int.Parse(port, CultureInfo.InvariantCulture)])
        : OperatingSystem.IsWindows() && options.Get("process") is { } process
            ? new ProcessConnectionFilter([process])
            : new AcceptAllFilter();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var source = new LiveCaptureSource(new LiveCaptureOptions { Device = options.Get("device"), RecordPath = output }, filter);
    await using (source)
    {
        Console.WriteLine($"Grabando en {output} (filtro: {filter.GetType().Name}). Ctrl+C para terminar.");
        long segments = 0, bytes = 0;
        await foreach (var segment in source.ReadAllAsync(cts.Token))
        {
            segments++;
            bytes += segment.Payload.Length;
            if (segments % 500 == 0)
            {
                Console.Write($"\r{segments} segmentos, {bytes / 1024} KiB, {source.Dropped} descartados");
            }
        }

        Console.WriteLine($"\nListo: {segments} segmentos, {bytes / 1024} KiB. Placa: {source.SelectedDevice ?? "(sin tráfico del juego)"}.");
    }

    return 0;
}

static async Task<int> DumpAsync(string input, Options options)
{
    var opcodesPath = options.Get("opcodes") ?? Path.Combine(AppContext.BaseDirectory, "data", "opcodes.json");
    var table = File.Exists(opcodesPath) ? OpcodeTable.Load(opcodesPath) : OpcodeTable.Empty;
    var filter = options.Get("opcode") is { } text ? ParseOpcode(text) : (ushort?)null;
    var samples = int.Parse(options.Get("samples") ?? "3", CultureInfo.InvariantCulture);

    var flows = new TcpFlowTracker();
    var framers = new Dictionary<FlowKey, PacketFramer>();
    var histogram = new Dictionary<(FlowKey Flow, ushort Opcode), (long Count, long Bytes)>();
    var shown = new Dictionary<ushort, int>();

    await using var source = new PcapFileSource(input);
    await foreach (var segment in source.ReadAllAsync(CancellationToken.None))
    {
        var result = flows.Push(segment);
        if (!framers.TryGetValue(segment.Flow, out var framer))
        {
            framers[segment.Flow] = framer = new PacketFramer();
        }

        if (result.GapDetected)
        {
            framer.Reset();
        }

        foreach (var chunk in result.Chunks)
        {
            foreach (var frame in Expand(framer.Push(chunk.Span), table))
            {
                var key = (segment.Flow, frame.Opcode);
                var (count, size) = histogram.GetValueOrDefault(key);
                histogram[key] = (count + 1, size + frame.Body.Length);

                if ((filter is null || filter == frame.Opcode) && shown.GetValueOrDefault(frame.Opcode) < samples)
                {
                    shown[frame.Opcode] = shown.GetValueOrDefault(frame.Opcode) + 1;
                    Console.WriteLine($"[{segment.Timestamp:HH:mm:ss.fff}] {segment.Flow} op=0x{frame.Opcode:X4} ({table.Resolve(frame.Opcode)}) {frame.Body.Length} bytes");
                    Console.WriteLine(HexDump(frame.Body.Span));
                }
            }
        }
    }

    Console.WriteLine("\nOpcodes por conexión (cantidad, bytes):");
    foreach (var ((flow, opcode), (count, size)) in histogram.OrderBy(h => h.Key.Flow.ToString()).ThenByDescending(h => h.Value.Count))
    {
        Console.WriteLine($"  {flow,-45} 0x{opcode:X4} {table.Resolve(opcode),-12} {count,8} {size,10}");
    }

    Console.WriteLine($"\nDesincronizaciones del framer: {framers.Values.Sum(f => f.Desyncs)}");
    return 0;
}

// Corre el pipeline completo (decoders + motor de combate) sobre una captura e imprime los encuentros.
static async Task<int> ReplayAsync(string input, Options options)
{
    var opcodesPath = options.Get("opcodes") ?? Path.Combine(AppContext.BaseDirectory, "data", "opcodes.json");
    var table = OpcodeTable.Load(opcodesPath);
    var engine = new CombatEngine();
    var encounters = new List<EncounterSnapshot>();
    engine.EncounterFinished += encounters.Add;

    var flows = new TcpFlowTracker();
    var streams = new Dictionary<FlowKey, GameStreamDecoder>();
    var stats = new DecoderStats();
    var events = new Dictionary<string, int>();
    DateTimeOffset last = default;

    await using var source = new PcapFileSource(input);
    await foreach (var segment in source.ReadAllAsync(CancellationToken.None))
    {
        last = segment.Timestamp;
        var result = flows.Push(segment);
        if (!segment.Flow.IsFromServer())
        {
            continue;
        }
        if (!streams.TryGetValue(segment.Flow, out var stream))
        {
            streams[segment.Flow] = stream = new GameStreamDecoder(table, PacketDecoders.All, stats: stats);
        }

        if (result.GapDetected)
        {
            stream.Resync();
        }

        foreach (var chunk in result.Chunks)
        {
            foreach (var e in stream.Push(chunk.Span, segment.Timestamp))
            {
                events[e.GetType().Name] = events.GetValueOrDefault(e.GetType().Name) + 1;
                engine.Apply(e);
            }
        }
    }

    engine.Tick(last + TimeSpan.FromMinutes(1));

    Console.WriteLine($"Eventos: {string.Join(", ", events.Select(kv => $"{kv.Key}={kv.Value}"))}");
    Console.WriteLine($"Errores de decodificación: {stats.Errors}\n");
    foreach (var encounter in encounters)
    {
        Console.WriteLine($"[{encounter.Start:HH:mm:ss}] {encounter.TargetName ?? "?"} · {encounter.Duration.TotalSeconds:0.0}s · {(encounter.TargetKilled ? "muerto" : "sin terminar")} · total {encounter.TotalDamage:N0}"
            + (encounter.MainTargetDuration is { } window ? $" · contra el objetivo {window.TotalSeconds:0.00}s" : string.Empty));
        foreach (var actor in encounter.Actors)
        {
            var c = actor.Counts ?? HitCounts.Empty;
            Console.WriteLine($"    {actor.Name,-20} {actor.Damage,10:N0}  {actor.Dps,8:N0} dps  {actor.Share,6:P0}  {actor.Hits,4} golpes"
                + $"  | objetivo {actor.MainTargetDamage,10:N0} {actor.MainTargetDps,8:N0} dps"
                + $"  | golpes {c.Hits} CRI {c.Critical} PERF {c.Perfect} DOUB {c.DoubleHit} FRNT {c.Front} BACK {c.Back} MULT {c.MultiHit}");
        }
    }

    return 0;
}

static IEnumerable<RawFrame> Expand(IEnumerable<RawFrame> frames, OpcodeTable table)
{
    foreach (var frame in frames)
    {
        yield return frame;
        if (table.Resolve(frame.Opcode) != PacketKind.Bundle)
        {
            continue;
        }

        IReadOnlyList<RawFrame> inner;
        try
        {
            inner = new PacketFramer().Push(BundleDecoder.Decompress(frame.Body.Span));
        }
        catch (InvalidDataException)
        {
            continue;
        }

        foreach (var child in Expand(inner, table))
        {
            yield return child;
        }
    }
}

static ushort ParseOpcode(string text) => text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
    ? ushort.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
    : ushort.Parse(text, CultureInfo.InvariantCulture);

static string HexDump(ReadOnlySpan<byte> data, int max = 256)
{
    var builder = new System.Text.StringBuilder();
    var length = Math.Min(data.Length, max);
    for (var row = 0; row < length; row += 16)
    {
        var slice = data.Slice(row, Math.Min(16, length - row));
        builder.Append(CultureInfo.InvariantCulture, $"    {row:X4}  ");
        foreach (var b in slice)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{b:X2} ");
        }

        builder.Append(' ', (16 - slice.Length) * 3).Append(' ');
        foreach (var b in slice)
        {
            builder.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }

        builder.AppendLine();
    }

    if (data.Length > max)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"    ... ({data.Length - max} bytes más)");
    }

    return builder.ToString();
}

internal sealed record Options(IReadOnlyDictionary<string, string> Values)
{
    public static Options Parse(IEnumerable<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? key = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                key = arg[2..];
                values[key] = string.Empty;
            }
            else if (key is not null)
            {
                values[key] = arg;
                key = null;
            }
        }

        return new Options(values);
    }

    public string? Get(string key) => Values.TryGetValue(key, out var value) && value.Length > 0 ? value : null;
}
