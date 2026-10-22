using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using BestCrush.Network.Protocol;

namespace BestCrush.NetworkProbe.Capture;

/// <summary>
/// Windows-only research bridge between a visible Dofus HWND/PID and the
/// owner's ESTABLISHED IPv4 TCP socket. Nothing is persisted or exposed
/// except anonymous window slots and categorical socket matches.
/// </summary>
internal static class WindowsDofusSocketTable
{
    private const uint ErrorInsufficientBuffer = 122;
    private const int AddressFamilyInet = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const int TcpStateEstablished = 5;
    private const int TcpStateSynSent = 3;
    private const int OwnerPidRowBytes = 24;
    private const int MaxTableBytes = 16 * 1024 * 1024;

    internal readonly record struct GameWindow(long Handle, int ProcessId);

    internal static IReadOnlyList<GameWindow> VisibleWindows()
    {
        List<GameWindow> result = [];
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
                return true;

            GetWindowThreadProcessId(handle, out uint pid);
            if (pid is 0 or > int.MaxValue)
                return true;

            try
            {
                using Process process = Process.GetProcessById((int)pid);
                if (process.ProcessName.Contains("dofus", StringComparison.OrdinalIgnoreCase))
                    result.Add(new GameWindow(handle.ToInt64(), (int)pid));
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    internal static IReadOnlyList<TcpSocketOwner> EstablishedIPv4(int gamePort) =>
        ReadOwners(gamePort, includeConnecting: false);

    /// <summary>
    /// Opt-in P3m.13d: permit SYN_SENT as well as ESTABLISHED when looking
    /// up a short-lived outgoing Connection flow on its initial SYN.
    /// Do not change P3k's established-only behavior.
    /// </summary>
    internal static IReadOnlyList<TcpSocketOwner> ConnectingOrEstablishedIPv4(
        int gamePort) => ReadOwners(gamePort, includeConnecting: true);

    private static IReadOnlyList<TcpSocketOwner> ReadOwners(
        int gamePort, bool includeConnecting)
    {
        int size = 0;
        uint result = GetExtendedTcpTable(
            IntPtr.Zero, ref size, true, AddressFamilyInet,
            TcpTableOwnerPidAll, 0);
        if (result != ErrorInsufficientBuffer || size is < 4 or > MaxTableBytes)
            return [];

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedTcpTable(
                buffer, ref size, true, AddressFamilyInet,
                TcpTableOwnerPidAll, 0);
            if (result != 0 || size is < 4 or > MaxTableBytes)
                return [];

            int rows = Marshal.ReadInt32(buffer);
            if (rows < 0 || rows > (size - 4) / OwnerPidRowBytes)
                return [];

            List<TcpSocketOwner> sockets = [];
            for (int i = 0; i < rows; i++)
            {
                int offset = 4 + i * OwnerPidRowBytes;
                int state = Marshal.ReadInt32(buffer, offset);
                if (state != TcpStateEstablished &&
                    (!includeConnecting || state != TcpStateSynSent))
                    continue;

                ushort localPort = ReadPort(buffer, offset + 8);
                ushort remotePort = ReadPort(buffer, offset + 16);
                int pid = Marshal.ReadInt32(buffer, offset + 20);
                if (remotePort != gamePort || localPort == 0 || pid <= 0)
                    continue;

                // The Windows MIB IPv4 address DWORD is stored in network
                // byte order within the native buffer.
                IPAddress local = new(BitConverter.GetBytes(Marshal.ReadInt32(buffer, offset + 4)));
                IPAddress remote = new(BitConverter.GetBytes(Marshal.ReadInt32(buffer, offset + 12)));
                sockets.Add(new TcpSocketOwner(
                    new TcpSocketTuple(local, localPort, remote, remotePort), pid));
            }
            return sockets;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ushort ReadPort(IntPtr table, int offset) =>
        (ushort)((Marshal.ReadByte(table, offset) << 8) |
                  Marshal.ReadByte(table, offset + 1));

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily, int tableClass, uint reserved);
}
