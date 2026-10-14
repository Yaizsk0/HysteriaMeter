using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HysteriaMeter.Capture.Filtering;

/// <summary>
/// Filtra por las conexiones TCP que pertenecen a un proceso (por nombre), consultando la tabla TCP
/// de Windows (<c>GetExtendedTcpTable</c>). Es la misma información que muestra <c>netstat -ano</c>:
/// no abre ni lee el proceso del juego.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class ProcessConnectionFilter : IConnectionFilter, IDisposable
{
    private readonly HashSet<string> _processNames;
    private readonly Timer _timer;
    private volatile HashSet<(IPEndPoint Local, IPEndPoint Remote)> _connections = [];

    public ProcessConnectionFilter(IEnumerable<string> processNames, TimeSpan? refreshInterval = null)
    {
        _processNames = new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);
        var interval = refreshInterval ?? TimeSpan.FromSeconds(2);
        _timer = new Timer(_ => Refresh(), null, TimeSpan.Zero, interval);
    }

    public int ConnectionCount => _connections.Count;

    public bool IsGameTraffic(in TcpSegment segment)
    {
        var connections = _connections;
        return connections.Contains((segment.Source, segment.Destination)) ||
               connections.Contains((segment.Destination, segment.Source));
    }

    public void Dispose() => _timer.Dispose();

    private void Refresh()
    {
        var pids = Process.GetProcesses()
            .Where(p => _processNames.Contains(p.ProcessName))
            .Select(p => (uint)p.Id)
            .ToHashSet();

        _connections = pids.Count == 0 ? [] : ReadTcpTable(pids);
    }

    private static HashSet<(IPEndPoint, IPEndPoint)> ReadTcpTable(HashSet<uint> pids)
    {
        const int AfInet = 2;
        const int TcpTableOwnerPidAll = 5;

        var result = new HashSet<(IPEndPoint, IPEndPoint)>();
        var size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0) != 0)
            {
                return result;
            }

            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<TcpRowOwnerPid>(buffer + 4 + (i * rowSize));
                if (pids.Contains(row.OwningPid))
                {
                    result.Add((
                        new IPEndPoint(new IPAddress(row.LocalAddr), NetworkPort(row.LocalPort)),
                        new IPEndPoint(new IPAddress(row.RemoteAddr), NetworkPort(row.RemotePort))));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

    private static int NetworkPort(uint raw) => (int)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));

    [LibraryImport("iphlpapi.dll", SetLastError = true)]
    private static partial uint GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int addressFamily, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TcpRowOwnerPid
    {
        public readonly uint State;
        public readonly uint LocalAddr;
        public readonly uint LocalPort;
        public readonly uint RemoteAddr;
        public readonly uint RemotePort;
        public readonly uint OwningPid;
    }
}
