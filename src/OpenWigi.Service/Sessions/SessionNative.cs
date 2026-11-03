using System.Runtime.InteropServices;

namespace OpenWigi.Service.Sessions;

internal static class SessionNative
{
    public const uint NoSession = 0xFFFFFFFF;

    // WTS_INFO_CLASS
    public const int WTSUserName = 5;
    public const int WTSConnectState = 8;
    public const int WTSClientProtocolType = 16;
    public const int WTSSessionInfoEx = 25;

    // WTS_CONNECTSTATE_CLASS
    public const int WTSActive = 0;
    public const int WTSDisconnected = 4;

    // WTSClientProtocolType
    public const ushort WTS_PROTOCOL_TYPE_CONSOLE = 0;

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

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSEnumerateSessionsW(nint server, int reserved, int version, out nint sessionInfo, out int count);

    [StructLayout(LayoutKind.Sequential)]
    private struct WTS_SESSION_INFOW
    {
        public int SessionId;
        public nint WinStationName;
        public int State;
    }

    /// <summary>Sessões da máquina e o estado da conexão de cada uma (WTS_CONNECTSTATE_CLASS), ou null se o Windows negar.</summary>
    public static IReadOnlyList<(int SessionId, int State)>? EnumerateSessions()
    {
        if (!WTSEnumerateSessionsW(0, 0, 1, out var buffer, out var count))
            return null;
        try
        {
            var size = Marshal.SizeOf<WTS_SESSION_INFOW>();
            var sessions = new List<(int, int)>(count);
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<WTS_SESSION_INFOW>(buffer + i * size);
                sessions.Add((info.SessionId, info.State));
            }
            return sessions;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    /// <summary>Usuário logado na sessão, ou vazio (sessão 0, tela de logon, listener).</summary>
    public static string UserName(int sessionId)
    {
        if (!WTSQuerySessionInformationW(0, sessionId, WTSUserName, out var buffer, out _))
            return string.Empty;
        try
        {
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    /// <summary>Sessão aberta por um cliente remoto (RDP), e não na frente do PC.</summary>
    public static bool IsRemote(int sessionId)
    {
        if (!WTSQuerySessionInformationW(0, sessionId, WTSClientProtocolType, out var buffer, out _))
            return false;
        try
        {
            return (ushort)Marshal.ReadInt16(buffer) != WTS_PROTOCOL_TYPE_CONSOLE;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);

    /// <summary>
    /// Sessão do cliente direto do pipe. Não precisa abrir o processo do cliente, o que o serviço
    /// rodando como LocalService não tem permissão de fazer com processos de outros usuários.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNamedPipeClientSessionId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientSessionId);

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
