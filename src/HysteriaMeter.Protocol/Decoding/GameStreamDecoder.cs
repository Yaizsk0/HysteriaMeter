using System.Collections.Concurrent;
using System.Collections.Frozen;
using HysteriaMeter.Protocol.Events;
using HysteriaMeter.Protocol.Framing;
using HysteriaMeter.Protocol.Opcodes;

namespace HysteriaMeter.Protocol.Decoding;

/// <summary>
/// Decodifica UNA dirección de UNA conexión: recibe bytes ya reensamblados y devuelve eventos.
/// Expande bundles LZ4 y despacha cada frame al decoder de su tipo.
/// </summary>
public sealed class GameStreamDecoder
{
    private const int MaxBundleDepth = 4;

    private readonly PacketFramer _framer;
    private readonly OpcodeTable _opcodes;
    private readonly ProtocolOptions _options;
    private readonly FrozenDictionary<PacketKind, IPacketDecoder> _decoders;
    private readonly DecoderStats _stats;

    public GameStreamDecoder(OpcodeTable opcodes, IEnumerable<IPacketDecoder> decoders, ProtocolOptions? options = null, DecoderStats? stats = null)
    {
        _options = options ?? ProtocolOptions.Default;
        _framer = new PacketFramer(_options);
        _opcodes = opcodes;
        _decoders = decoders.ToFrozenDictionary(d => d.Kind);
        _stats = stats ?? new DecoderStats();
    }

    public DecoderStats Stats => _stats;

    public IReadOnlyList<CombatEvent> Push(ReadOnlySpan<byte> data, DateTimeOffset timestamp)
    {
        var events = new List<CombatEvent>();
        foreach (var frame in _framer.Push(data))
        {
            Handle(frame, timestamp, events, depth: 0);
        }

        return events;
    }

    /// <summary>Llamar cuando el reensamblador reporta un hueco: el estado parcial ya no sirve.</summary>
    public void Resync() => _framer.Reset();

    private void Handle(RawFrame frame, DateTimeOffset timestamp, List<CombatEvent> events, int depth)
    {
        var kind = _opcodes.Resolve(frame.Opcode);
        _stats.Count(frame.Opcode, kind);

        if (kind == PacketKind.Bundle)
        {
            if (depth >= MaxBundleDepth)
            {
                _stats.Errors++;
                return;
            }

            var inner = new PacketFramer(_options);
            try
            {
                foreach (var child in inner.Push(BundleDecoder.Decompress(frame.Body.Span)))
                {
                    Handle(child, timestamp, events, depth + 1);
                }
            }
            catch (InvalidDataException)
            {
                _stats.Errors++;
            }

            return;
        }

        if (!_decoders.TryGetValue(kind, out var decoder))
        {
            return;
        }

        try
        {
            decoder.Decode(frame.Body.Span, timestamp, events);
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or OverflowException)
        {
            // Un paquete mal interpretado no debe tumbar el meter; queda contado para diagnóstico.
            _stats.Errors++;
        }
    }
}

/// <summary>Contadores para descubrir el protocolo: qué opcodes aparecen y cuántos fallan al decodificar.</summary>
public sealed class DecoderStats
{
    private readonly ConcurrentDictionary<ushort, long> _byOpcode = new();

    public long Errors { get; set; }

    public long UnknownFrames { get; private set; }

    public IReadOnlyDictionary<ushort, long> ByOpcode => _byOpcode;

    internal void Count(ushort opcode, PacketKind kind)
    {
        _byOpcode.AddOrUpdate(opcode, 1, (_, n) => n + 1);
        if (kind == PacketKind.Unknown)
        {
            UnknownFrames++;
        }
    }
}
