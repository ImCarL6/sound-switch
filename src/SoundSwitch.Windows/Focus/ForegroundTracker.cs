using System.Runtime.InteropServices;
using SoundSwitch.Core.Focus;
using SoundSwitch.Windows.Diagnostics;

namespace SoundSwitch.Windows.Focus;

/// <summary>
/// Acompanha a janela em primeiro plano por evento (SetWinEventHook), sem polling (RNF5).
/// Precisa ser criado numa thread com loop de mensagens (a thread de UI do WPF).
/// Janelas do próprio processo (overlay, menu da bandeja) são ignoradas: o foco anterior continua valendo (RF3.3).
/// </summary>
public sealed class ForegroundTracker : IDisposable
{
    const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;

    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumChildProc proc, IntPtr lParam);

    readonly WinEventProc proc; // mantido vivo enquanto o hook existir
    readonly IntPtr hook;
    readonly int ownPid = Environment.ProcessId;

    /// <summary>App em foco atual (null até a primeira janela "de verdade").</summary>
    public FocusedApp? Current { get; private set; }

    /// <summary>Disparado na thread de UI quando o app em foco muda.</summary>
    public event Action<FocusedApp>? Changed;

    public ForegroundTracker()
    {
        proc = OnEvent;
        hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, proc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (hook == IntPtr.Zero) Log.Error("Foco: SetWinEventHook falhou; o fader do app em foco não vai acompanhar trocas de janela.");
        Update(GetForegroundWindow());
    }

    void OnEvent(IntPtr h, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try { Update(hwnd); }
        catch (Exception ex) { Log.Error("Foco: erro ao tratar troca de janela", ex); }
    }

    void Update(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        GetWindowThreadProcessId(hwnd, out int pid);
        if (pid == 0 || pid == ownPid) return;

        var path = ProcessInfo.ExePath(pid);

        // Apps UWP/Store: a janela é do ApplicationFrameHost; o app real é dono de uma janela filha.
        if (path is not null && Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            int hostPid = pid, realPid = 0;
            EnumChildWindows(hwnd, (child, _) =>
            {
                GetWindowThreadProcessId(child, out int cp);
                if (cp != hostPid && cp != 0) { realPid = cp; return false; }
                return true;
            }, IntPtr.Zero);
            if (realPid != 0) { pid = realPid; path = ProcessInfo.ExePath(pid); }
        }

        if (Current is { } cur && cur.Pid == pid) return;

        var app = new FocusedApp(pid, path, ProcessInfo.DisplayName(path, pid));
        Current = app;
        Log.Debug($"Foco: {app.DisplayName} (pid {app.Pid}, {app.ExePath ?? "caminho indisponível"})");
        Changed?.Invoke(app);
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) UnhookWinEvent(hook);
    }
}
