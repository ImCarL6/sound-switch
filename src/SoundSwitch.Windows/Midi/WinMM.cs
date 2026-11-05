using System.Runtime.InteropServices;

namespace SoundSwitch.Windows.Midi;

/// <summary>Interop mínimo com midiIn* (WinMM). Mesmo código validado no tools/MidiMonitor.</summary>
internal static class WinMM
{
    public const int MMSYSERR_NOERROR = 0;
    public const int CALLBACK_FUNCTION = 0x00030000;
    public const uint MIM_OPEN = 0x3C1, MIM_CLOSE = 0x3C2, MIM_DATA = 0x3C3, MIM_ERROR = 0x3C5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MIDIINCAPS
    {
        public ushort wMid, wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint dwSupport;
    }

    public delegate void MidiInProc(IntPtr hMidiIn, uint wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

    [DllImport("winmm.dll")] static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int midiInGetDevCapsW(IntPtr uDeviceID, out MIDIINCAPS caps, uint cb);
    [DllImport("winmm.dll")] public static extern int midiInOpen(out IntPtr lphMidiIn, uint uDeviceID, MidiInProc dwCallback, IntPtr dwInstance, int dwFlags);
    [DllImport("winmm.dll")] public static extern int midiInStart(IntPtr hMidiIn);
    [DllImport("winmm.dll")] public static extern int midiInStop(IntPtr hMidiIn);
    [DllImport("winmm.dll")] public static extern int midiInReset(IntPtr hMidiIn);
    [DllImport("winmm.dll")] public static extern int midiInClose(IntPtr hMidiIn);
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

    public static string ErrorText(int err)
    {
        var buf = new char[256];
        return midiInGetErrorTextW(err, buf, buf.Length) == MMSYSERR_NOERROR
            ? new string(buf).TrimEnd('\0') : $"MMSYSERR {err}";
    }
}
