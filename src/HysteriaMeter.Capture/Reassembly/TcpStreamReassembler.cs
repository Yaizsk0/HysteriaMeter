namespace HysteriaMeter.Capture.Reassembly;

/// <summary>
/// Reensambla UNA dirección de una conexión TCP: ordena segmentos, descarta retransmisiones y solapes,
/// y entrega bytes contiguos. Si un hueco no se completa antes de <see cref="MaxPendingBytes"/>,
/// lo saltea y lo informa para que el decoder se resincronice.
/// </summary>
public sealed class TcpStreamReassembler(int maxPendingBytes = 1 << 20)
{
    private readonly Dictionary<uint, byte[]> _pending = [];
    private uint? _next;
    private int _pendingBytes;

    public int MaxPendingBytes { get; } = maxPendingBytes;

    public long Gaps { get; private set; }

    public ReassemblyResult Push(in TcpSegment segment)
    {
        if (segment.Syn)
        {
            _next = segment.Sequence + 1;
            ClearPending();
            return ReassemblyResult.Empty;
        }

        if (segment.Payload.IsEmpty)
        {
            return ReassemblyResult.Empty;
        }

        // Tomamos la conexión a mitad de camino: arrancamos donde estemos.
        _next ??= segment.Sequence;

        var chunks = new List<ReadOnlyMemory<byte>>();
        var gap = false;
        var offset = Distance(segment.Sequence, _next.Value);

        if (offset > 0)
        {
            if (_pending.TryAdd(segment.Sequence, segment.Payload.ToArray()))
            {
                _pendingBytes += segment.Payload.Length;
            }

            if (_pendingBytes <= MaxPendingBytes)
            {
                return ReassemblyResult.Empty;
            }

            // El hueco no se va a completar: saltamos al segmento pendiente más antiguo.
            _next = _pending.Keys.MinBy(s => Distance(s, _next.Value));
            Gaps++;
            gap = true;
        }
        else
        {
            Append(segment.Sequence, segment.Payload, chunks);
        }

        Drain(chunks);
        return new ReassemblyResult(chunks, gap);
    }

    private void Drain(List<ReadOnlyMemory<byte>> chunks)
    {
        bool progressed;
        do
        {
            progressed = false;
            foreach (var (seq, data) in _pending)
            {
                if (Distance(seq, _next!.Value) <= 0)
                {
                    _pending.Remove(seq);
                    _pendingBytes -= data.Length;
                    Append(seq, data, chunks);
                    progressed = true;
                    break;
                }
            }
        }
        while (progressed);
    }

    private void Append(uint sequence, ReadOnlyMemory<byte> payload, List<ReadOnlyMemory<byte>> chunks)
    {
        var offset = Distance(sequence, _next!.Value);
        if (offset + payload.Length <= 0)
        {
            return; // retransmisión completa: ya lo teníamos
        }

        var fresh = payload[(int)-offset..];
        chunks.Add(fresh);
        _next = unchecked(_next.Value + (uint)fresh.Length);
    }

    private void ClearPending()
    {
        _pending.Clear();
        _pendingBytes = 0;
    }

    /// <summary>Diferencia con signo en el espacio de secuencias de 32 bits (maneja el wraparound).</summary>
    private static long Distance(uint sequence, uint reference) => unchecked((int)(sequence - reference));
}

public readonly record struct ReassemblyResult(IReadOnlyList<ReadOnlyMemory<byte>> Chunks, bool GapDetected)
{
    public static ReassemblyResult Empty { get; } = new([], false);
}
