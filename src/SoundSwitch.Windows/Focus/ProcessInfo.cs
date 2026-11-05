using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SoundSwitch.Windows.Focus;

/// <summary>Consultas baratas sobre processos, sem precisar de admin.</summary>
public static class ProcessInfo
{
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint TH32CS_SNAPPROCESS = 0x2;

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    /// <summary>Caminho completo do executável, ou null (processo protegido/encerrado).</summary>
    public static string? ExePath(int pid)
    {
        if (pid <= 4) return null;
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Snapshot PID → PID do pai de todos os processos.</summary>
    public static Dictionary<int, int> ParentMap()
    {
        var map = new Dictionary<int, int>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return map;
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snap, ref e)) return map;
            do map[(int)e.th32ProcessID] = (int)e.th32ParentProcessID;
            while (Process32NextW(snap, ref e));
        }
        finally { CloseHandle(snap); }
        return map;
    }

    /// <summary>Nome amigável: descrição do arquivo ("Spotify", "Google Chrome"), senão o nome do exe.</summary>
    public static string DisplayName(string? exePath, int pid)
    {
        if (exePath is not null)
        {
            try
            {
                var desc = FileVersionInfo.GetVersionInfo(exePath).FileDescription?.Trim();
                if (!string.IsNullOrEmpty(desc)) return desc;
            }
            catch { /* sem versão */ }
            return Path.GetFileNameWithoutExtension(exePath);
        }
        try { return Process.GetProcessById(pid).ProcessName; }
        catch { return $"PID {pid}"; }
    }
}
