using SoundSwitch.Core.Config;

namespace SoundSwitch.Core.Routing;

/// <summary>Mensagem MIDI curta decodificada do formato WinMM (status no byte baixo).</summary>
public readonly record struct MidiMessage(byte Status, byte Data1, byte Data2)
{
    public static MidiMessage FromWinMM(uint packed) =>
        new((byte)(packed & 0xFF), (byte)((packed >> 8) & 0x7F), (byte)((packed >> 16) & 0x7F));

    public bool IsControlChange => (Status & 0xF0) == 0xB0;

    /// <summary>Canal 1–16.</summary>
    public int Channel => (Status & 0x0F) + 1;

    public override string ToString() => $"{Status:X2} {Data1:X2} {Data2:X2}";
}

public abstract record RoutedAction(int ControlIndex, ControlConfig Control);

/// <summary>Fader mexeu: <paramref name="Raw"/> é o valor 0–127 recebido.</summary>
public sealed record FaderMoved(int ControlIndex, ControlConfig Control, int Raw) : RoutedAction(ControlIndex, Control);

/// <summary>Botão disparou (já passou pelo triggerValue e pelo debounce).</summary>
public sealed record ButtonPressed(int ControlIndex, ControlConfig Control) : RoutedAction(ControlIndex, Control);

/// <summary>
/// Traduz mensagens MIDI em ações conforme o config. Thread-safe para um único produtor
/// (o callback do WinMM). Não faz I/O.
/// </summary>
public sealed class MidiRouter
{
    readonly int channel;
    readonly (int Index, ControlConfig Control)?[] byCc = new (int, ControlConfig)?[128];
    readonly int[] lastRaw;
    readonly long[] lastPressMs;
    readonly Func<long> nowMs;

    public MidiRouter(AppConfig config, Func<long>? clockMs = null)
    {
        channel = config.Midi.Channel;
        for (int i = 0; i < config.Controls.Count; i++)
            byCc[config.Controls[i].Cc] = (i, config.Controls[i]);
        lastRaw = Enumerable.Repeat(-1, config.Controls.Count).ToArray();
        lastPressMs = Enumerable.Repeat(long.MinValue / 2, config.Controls.Count).ToArray();
        nowMs = clockMs ?? (() => Environment.TickCount64);
    }

    public RoutedAction? Route(MidiMessage msg)
    {
        if (!msg.IsControlChange || msg.Channel != channel) return null;
        if (byCc[msg.Data1] is not { } entry) return null;
        var (index, control) = entry;

        int value = msg.Data2;
        if (control.Type == ControlTypes.Fader)
        {
            // Valor igual ao anterior não muda nada (evita trabalho e overlay piscando).
            if (lastRaw[index] == value) return null;
            lastRaw[index] = value;
            return new FaderMoved(index, control, value);
        }

        if (control.Type == ControlTypes.Button)
        {
            if (value != control.TriggerValue) return null; // ex.: o 0 da soltura
            long now = nowMs();
            if (now - lastPressMs[index] < control.DebounceMs) return null;
            lastPressMs[index] = now;
            return new ButtonPressed(index, control);
        }

        return null;
    }
}
