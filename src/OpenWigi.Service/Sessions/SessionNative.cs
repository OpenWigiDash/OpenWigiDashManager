using System.Runtime.InteropServices;

namespace OpenWigi.Service.Sessions;

internal static class SessionNative
{
    public const uint NoSession = 0xFFFFFFFF;

    // WTS_INFO_CLASS
    public const int WTSConnectState = 8;
    public const int WTSSessionInfoEx = 25;

    // WTS_CONNECTSTATE_CLASS
    public const int WTSActive = 0;

    // WTSINFOEX_LEVEL1.SessionFlags (a partir do Windows 8)
    public const int WTS_SESSIONSTATE_LOCK = 0;
    public const int WTS_SESSIONSTATE_UNLOCK = 1;

    [DllImport("kernel32.dll")]
    public static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSQuerySessionInformationW(nint server, int sessionId, int infoClass, out nint buffer, out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    public static extern void WTSFreeMemory(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);

    public static bool IsActive(int sessionId)
    {
        if (!WTSQuerySessionInformationW(0, sessionId, WTSConnectState, out var buffer, out _))
            return false;
        try
        {
            return Marshal.ReadInt32(buffer) == WTSActive;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    /// <summary>
    /// Lê o estado de bloqueio. Em WTSINFOEXW, Level (u32) vem seguido da união alinhada em 8;
    /// dentro dela SessionId e SessionState ocupam 8 bytes, então SessionFlags fica no offset 16.
    /// </summary>
    public static bool? IsLocked(int sessionId)
    {
        if (!WTSQuerySessionInformationW(0, sessionId, WTSSessionInfoEx, out var buffer, out _))
            return null;
        try
        {
            var flags = Marshal.ReadInt32(buffer, 16);
            return flags switch
            {
                WTS_SESSIONSTATE_LOCK => true,
                WTS_SESSIONSTATE_UNLOCK => false,
                _ => null,
            };
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }
}
