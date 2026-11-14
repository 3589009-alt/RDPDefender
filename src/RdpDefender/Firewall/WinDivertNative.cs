using System.Runtime.InteropServices;

namespace RdpDefender.Firewall;

/// <summary>Минимальные P/Invoke-объявления WinDivert 2.2.2 (x64).</summary>
internal static class WinDivertNative
{
    public const int LayerNetwork = 0;
    public const int ShutdownRecv = 1;
    public const int ParamQueueLength = 0;
    public const int ParamQueueTime = 1;

    public static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("WinDivert.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr WinDivertOpen(
        [MarshalAs(UnmanagedType.LPStr)] string filter,
        int layer,
        short priority,
        ulong flags);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertRecv(
        IntPtr handle,
        byte[] packet,
        uint packetLen,
        out uint recvLen,
        IntPtr address);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertShutdown(IntPtr handle, int how);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertClose(IntPtr handle);

    [DllImport("WinDivert.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertSetParam(IntPtr handle, int param, ulong value);

    [DllImport("WinDivert.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertHelperCompileFilter(
        [MarshalAs(UnmanagedType.LPStr)] string filter,
        int layer,
        IntPtr obj,
        uint objLen,
        out IntPtr errorString,
        out uint errorPosition);
}
