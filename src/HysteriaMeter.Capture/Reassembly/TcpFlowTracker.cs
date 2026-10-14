namespace HysteriaMeter.Capture.Reassembly;

/// <summary>Mantiene un reensamblador por cada dirección de cada conexión.</summary>
public sealed class TcpFlowTracker
{
    private readonly Dictionary<FlowKey, TcpStreamReassembler> _flows = [];

    public IReadOnlyCollection<FlowKey> Flows => _flows.Keys;

    public ReassemblyResult Push(in TcpSegment segment)
    {
        var key = segment.Flow;
        if (segment.Rst)
        {
            _flows.Remove(key);
            _flows.Remove(key.Reverse);
            return ReassemblyResult.Empty;
        }

        if (!_flows.TryGetValue(key, out var reassembler))
        {
            reassembler = new TcpStreamReassembler();
            _flows[key] = reassembler;
        }

        var result = reassembler.Push(segment);
        if (segment.Fin)
        {
            _flows.Remove(key);
        }

        return result;
    }
}
