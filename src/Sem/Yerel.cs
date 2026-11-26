using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sem.Ajan
{
    /// <summary>Windows API çağrıları.</summary>
    static class Yerel
    {
        [DllImport("kernel32.dll")] public static extern uint WTSGetActiveConsoleSessionId();
        [DllImport("wtsapi32.dll", SetLastError = true)] static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attrs, int impLevel, int tokenType, out IntPtr newToken);
        [DllImport("userenv.dll", SetLastError = true)] static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, bool inherit);
        [DllImport("userenv.dll", SetLastError = true)] static extern bool DestroyEnvironmentBlock(IntPtr env);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint esFlags);
        public const uint ES_SYSTEM_REQUIRED = 0x00000001;
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint param, string vparam, uint winIni);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SystemParametersInfo(uint action, uint param, System.Text.StringBuilder vparam, uint winIni);

        public const uint SPI_SETDESKWALLPAPER = 0x0014, SPI_GETDESKWALLPAPER = 0x0073, SPIF_UPDATEINIFILE = 1, SPIF_SENDCHANGE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessAsUser(IntPtr token, string app, string cmd, IntPtr pa, IntPtr ta, bool inherit,
            uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

        /// <summary>Hizmetten (SYSTEM), o an ekranda oturum açmış kullanıcının masaüstünde bir program başlatır.</summary>
        public static bool KullanicidaBaslat(string exe, string argumanlar)
        {
            uint oturum = WTSGetActiveConsoleSessionId();
            if (oturum == 0xFFFFFFFF) return false;
            IntPtr kTok, dTok = IntPtr.Zero, env = IntPtr.Zero;
            if (!WTSQueryUserToken(oturum, out kTok)) return false; // kimse oturum açmamış
            try
            {
                if (!DuplicateTokenEx(kTok, 0x02000000 /*MAXIMUM_ALLOWED*/, IntPtr.Zero, 2, 1 /*Primary*/, out dTok)) return false;
                CreateEnvironmentBlock(out env, dTok, false);
                var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), lpDesktop = "winsta0\\default" };
                PROCESS_INFORMATION pi;
                const uint CREATE_UNICODE_ENVIRONMENT = 0x400, CREATE_NO_WINDOW = 0x08000000;
                if (!CreateProcessAsUser(dTok, exe, "\"" + exe + "\" " + argumanlar, IntPtr.Zero, IntPtr.Zero, false,
                        CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW, env, System.IO.Path.GetDirectoryName(exe), ref si, out pi))
                    throw new Win32Exception();
                CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
                return true;
            }
            finally
            {
                if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
                if (dTok != IntPtr.Zero) CloseHandle(dTok);
                CloseHandle(kTok);
            }
        }
    }
}
