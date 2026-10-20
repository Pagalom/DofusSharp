using System.Net;
using System.Runtime.InteropServices;

namespace BestCrush.Network.Protocol;

/// <summary>
/// Read the local OS IPv4 TCP table with owner PIDs. Windows-only, in-memory,
/// with no logging of addresses/ports, payloads, tokens, or process metadata.
/// </summary>
public static class WindowsTcpSocketTableReader
{
    private const uint BufferTooSmall = 122;
    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const int TcpEstablished = 5;
    private const int RowBytes = 24;
    private const int MaxTableBytes = 16 * 1024 * 1024;

    public static IReadOnlyList<TcpSocketOwner> ReadEstablishedIPv4(int remotePort)
    {
        if (!OperatingSystem.IsWindows() || remotePort is < 1 or > 65535)
            return [];

        int bytes = 0;
        uint result = GetExtendedTcpTable(
            IntPtr.Zero, ref bytes, true, AfInet, TcpTableOwnerPidAll, 0);
        if (result != BufferTooSmall || bytes is < 4 or > MaxTableBytes)
            return [];

        IntPtr buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            result = GetExtendedTcpTable(
                buffer, ref bytes, true, AfInet, TcpTableOwnerPidAll, 0);
            if (result != 0 || bytes is < 4 or > MaxTableBytes)
                return [];

            int count = Marshal.ReadInt32(buffer);
            if (count < 0 || count > (bytes - 4) / RowBytes)
                return [];

            List<TcpSocketOwner> owners = [];
            for (int i = 0; i < count; i++)
            {
                int row = 4 + i * RowBytes;
                if (Marshal.ReadInt32(buffer, row) != TcpEstablished)
                    continue;

                ushort localPort = ReadPort(buffer, row + 8);
                ushort serverPort = ReadPort(buffer, row + 16);
                int pid = Marshal.ReadInt32(buffer, row + 20);
                if (localPort == 0 || serverPort != remotePort || pid <= 0)
                    continue;

                // MIB_TCPROW_OWNER_PID: address DWORDs are network-ordered
                // in their native buffer; preserve byte sequence as-is.
                IPAddress local = new(BitConverter.GetBytes(
                    Marshal.ReadInt32(buffer, row + 4)));
                IPAddress remote = new(BitConverter.GetBytes(
                    Marshal.ReadInt32(buffer, row + 12)));

                owners.Add(new TcpSocketOwner(
                    new TcpSocketTuple(local, localPort, remote, serverPort),
                    pid));
            }
            return owners;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ushort ReadPort(IntPtr buffer, int offset) =>
        (ushort)((Marshal.ReadByte(buffer, offset) << 8) |
                  Marshal.ReadByte(buffer, offset + 1));

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(
        IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool ordered,
        int addressFamily, int tableClass, uint reserved);
}
