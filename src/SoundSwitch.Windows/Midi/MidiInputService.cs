using SoundSwitch.Core.Routing;
using SoundSwitch.Windows.Diagnostics;

namespace SoundSwitch.Windows.Midi;

/// <summary>
/// Mantém a entrada MIDI aberta pelo nome e reconecta sozinho (RF1.1, RF1.2).
/// Um watchdog a cada 3 s: se desconectado, tenta abrir; se conectado, confere se o dispositivo
/// ainda está na lista do Windows. <see cref="MessageReceived"/> é chamado na thread do driver:
/// os assinantes não podem bloquear.
/// </summary>
public sealed class MidiInputService : IDisposable
{
    static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(3);

    readonly object gate = new();
    readonly Timer watchdog;
    readonly WinMM.MidiInProc callback; // mantido vivo enquanto houver handle aberto
    string wantedName;
    IntPtr handle;
    string? openName;
    bool disposed;

    public event Action<MidiMessage>? MessageReceived;

    /// <summary>(conectado, nome do dispositivo). Chamado fora da thread de UI.</summary>
    public event Action<bool, string?>? ConnectionChanged;

    public bool IsConnected { get { lock (gate) return handle != IntPtr.Zero; } }
    public string? DeviceName { get { lock (gate) return openName; } }

    public MidiInputService(string inputName)
    {
        wantedName = inputName;
        callback = OnMidi;
        watchdog = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        Tick();
        watchdog.Change(WatchdogInterval, WatchdogInterval);
    }

    /// <summary>Troca o nome procurado (recarga de config). Reabre se mudou.</summary>
    public void SetInputName(string inputName)
    {
        lock (gate)
        {
            if (string.Equals(wantedName, inputName, StringComparison.OrdinalIgnoreCase)) return;
            wantedName = inputName;
        }
        Reopen();
    }

    /// <summary>Fecha e abre de novo (ex.: volta da suspensão).</summary>
    public void Reopen()
    {
        CloseHandle("reabrindo");
        Tick();
    }

    void Tick()
    {
        bool raiseConnected = false;
        string? name = null;
        lock (gate)
        {
            if (disposed) return;
            var inputs = WinMM.ListInputs();

            if (handle != IntPtr.Zero)
            {
                if (inputs.Any(i => i.Name == openName)) return; // continua presente
                Log.Warn($"MIDI: \"{openName}\" sumiu da lista de dispositivos.");
                CloseLocked();
                ConnectionChanged?.Invoke(false, null);
            }

            var match = inputs.FirstOrDefault(i => i.Name.Contains(wantedName, StringComparison.OrdinalIgnoreCase));
            if (match.Name is null) return;

            int err = WinMM.midiInOpen(out var h, match.Id, callback, IntPtr.Zero, WinMM.CALLBACK_FUNCTION);
            if (err != WinMM.MMSYSERR_NOERROR)
            {
                Log.Warn($"MIDI: falha ao abrir \"{match.Name}\": {WinMM.ErrorText(err)}");
                return;
            }
            err = WinMM.midiInStart(h);
            if (err != WinMM.MMSYSERR_NOERROR)
            {
                WinMM.midiInClose(h);
                Log.Warn($"MIDI: falha ao iniciar \"{match.Name}\": {WinMM.ErrorText(err)}");
                return;
            }
            handle = h;
            openName = name = match.Name;
            raiseConnected = true;
            Log.Info($"MIDI: conectado a \"{match.Name}\" (entrada {match.Id}).");
        }
        if (raiseConnected) ConnectionChanged?.Invoke(true, name);
    }

    // Thread do driver: nada de chamar midiIn* aqui.
    void OnMidi(IntPtr h, uint msg, IntPtr inst, IntPtr p1, IntPtr p2)
    {
        switch (msg)
        {
            case WinMM.MIM_DATA:
                try { MessageReceived?.Invoke(MidiMessage.FromWinMM((uint)p1.ToInt64())); }
                catch (Exception ex) { Log.Error("MIDI: erro ao tratar mensagem", ex); }
                break;
            case WinMM.MIM_CLOSE:
                // Pode ser o nosso próprio midiInClose; o Tick decide se precisa reabrir.
                ThreadPool.QueueUserWorkItem(_ => { if (IsConnected && h == CurrentHandle) { CloseHandle("driver fechou a entrada"); Tick(); } });
                break;
        }
    }

    IntPtr CurrentHandle { get { lock (gate) return handle; } }

    void CloseHandle(string reason)
    {
        bool was;
        lock (gate)
        {
            was = handle != IntPtr.Zero;
            if (was) Log.Info($"MIDI: fechando \"{openName}\" ({reason}).");
            CloseLocked();
        }
        if (was) ConnectionChanged?.Invoke(false, null);
    }

    void CloseLocked()
    {
        if (handle == IntPtr.Zero) return;
        var h = handle;
        handle = IntPtr.Zero;
        openName = null;
        WinMM.midiInStop(h);
        WinMM.midiInReset(h);
        WinMM.midiInClose(h);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        watchdog.Dispose();
        lock (gate) CloseLocked();
    }
}
