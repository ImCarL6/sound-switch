using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MidiMonitor;

/// <summary>Interop mínimo com a API midiIn* do WinMM.</summary>
static class WinMM
{
    const int MMSYSERR_NOERROR = 0;
    const int CALLBACK_FUNCTION = 0x00030000;
    const uint MIM_OPEN = 0x3C1, MIM_CLOSE = 0x3C2, MIM_DATA = 0x3C3, MIM_LONGDATA = 0x3C4, MIM_ERROR = 0x3C5, MIM_LONGERROR = 0x3C6;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MIDIINCAPS
    {
        public ushort wMid, wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint dwSupport;
    }

    delegate void MidiInProc(IntPtr hMidiIn, uint wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

    [DllImport("winmm.dll")] static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int midiInGetDevCapsW(IntPtr uDeviceID, out MIDIINCAPS caps, uint cbMidiInCaps);
    [DllImport("winmm.dll")] static extern int midiInOpen(out IntPtr lphMidiIn, uint uDeviceID, MidiInProc dwCallback, IntPtr dwInstance, int dwFlags);
    [DllImport("winmm.dll")] static extern int midiInStart(IntPtr hMidiIn);
    [DllImport("winmm.dll")] static extern int midiInStop(IntPtr hMidiIn);
    [DllImport("winmm.dll")] static extern int midiInReset(IntPtr hMidiIn);
    [DllImport("winmm.dll")] static extern int midiInClose(IntPtr hMidiIn);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int midiInGetErrorTextW(int err, char[] text, int cch);

    public static List<(uint Id, string Name)> ListInputs()
    {
        var list = new List<(uint, string)>();
        uint n = midiInGetNumDevs();
        for (uint i = 0; i < n; i++)
            if (midiInGetDevCapsW((IntPtr)i, out var caps, (uint)Marshal.SizeOf<MIDIINCAPS>()) == MMSYSERR_NOERROR)
                list.Add((i, caps.szPname));
        return list;
    }

    static string ErrorText(int err)
    {
        var buf = new char[256];
        return midiInGetErrorTextW(err, buf, buf.Length) == MMSYSERR_NOERROR
            ? new string(buf).TrimEnd('\0') : $"erro MMSYSERR {err}";
    }

    public sealed class MidiInput(uint deviceId, BlockingCollection<MidiEvent> sink, Stopwatch clock) : IDisposable
    {
        IntPtr handle;
        MidiInProc? callback; // referência mantida viva enquanto o handle estiver aberto (evita GC do delegate)

        public void Open()
        {
            callback = OnMessage;
            int err = midiInOpen(out handle, deviceId, callback, IntPtr.Zero, CALLBACK_FUNCTION);
            if (err != MMSYSERR_NOERROR) throw new InvalidOperationException(ErrorText(err));
            err = midiInStart(handle);
            if (err != MMSYSERR_NOERROR) { midiInClose(handle); handle = IntPtr.Zero; throw new InvalidOperationException(ErrorText(err)); }
        }

        // Roda na thread do driver: só enfileira, não faz I/O nem chama outras funções do WinMM.
        void OnMessage(IntPtr h, uint msg, IntPtr inst, IntPtr p1, IntPtr p2)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            switch (msg)
            {
                case MIM_DATA: sink.Add(new MidiEvent(EventKind.Data, (uint)p1.ToInt64(), (uint)p2.ToInt64(), now)); break;
                case MIM_ERROR: sink.Add(new MidiEvent(EventKind.Error, (uint)p1.ToInt64(), (uint)p2.ToInt64(), now, $"mensagem inválida {(uint)p1.ToInt64():X6}")); break;
                case MIM_LONGDATA or MIM_LONGERROR: sink.Add(new MidiEvent(EventKind.LongData, 0, 0, now, "SysEx recebido (ignorado)")); break;
                case MIM_CLOSE: sink.Add(new MidiEvent(EventKind.Closed, 0, 0, now, "dispositivo fechado/desconectado")); break;
                case MIM_OPEN: break;
            }
        }

        public void Close()
        {
            if (handle == IntPtr.Zero) return;
            midiInStop(handle);
            midiInReset(handle);
            midiInClose(handle);
            handle = IntPtr.Zero;
        }

        public void Dispose() => Close();
    }
}
