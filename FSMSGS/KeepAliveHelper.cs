using System.Net.Sockets;
using System.Runtime.InteropServices;

public static class LinuxKeepAlive
{
    private const int SOL_TCP = 6;
    private const int TCP_KEEPIDLE = 4;
    private const int TCP_KEEPINTVL = 5;
    private const int TCP_KEEPCNT = 6;

    [DllImport("libc", SetLastError = true)]
    private static extern int setsockopt(IntPtr socket, int level, int option_name, ref int option_value, uint option_len);

    public static void EnableFastKeepAlive(Socket socket, int idleSeconds = 5, int intervalSeconds = 1, int probeCount = 3)
    {
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        IntPtr handle = socket.Handle;

        setsockopt(handle, SOL_TCP, TCP_KEEPIDLE, ref idleSeconds, sizeof(int));
        setsockopt(handle, SOL_TCP, TCP_KEEPINTVL, ref intervalSeconds, sizeof(int));
        setsockopt(handle, SOL_TCP, TCP_KEEPCNT, ref probeCount, sizeof(int));
    }
}

public static class KeepAliveHelper
{
    public static void EnableFastKeepAlive(Socket socket, int idleSeconds = 5, int intervalSeconds = 1, int probeCount = 3)
    {
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Convert seconds → milliseconds
            EnableFastKeepAliveWindows(socket, idleSeconds * 1000, intervalSeconds * 1000);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            LinuxKeepAlive.EnableFastKeepAlive(socket, idleSeconds, intervalSeconds, probeCount);
        }
    }

    private static void EnableFastKeepAliveWindows(Socket socket, int timeMs, int intervalMs)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            byte[] inOptionValues = new byte[12];
            BitConverter.GetBytes((uint)1).CopyTo(inOptionValues, 0);
            BitConverter.GetBytes((uint)timeMs).CopyTo(inOptionValues, 4);
            BitConverter.GetBytes((uint)intervalMs).CopyTo(inOptionValues, 8);
            socket.IOControl(IOControlCode.KeepAliveValues, inOptionValues, Array.Empty<byte>());
        }
    }
}
