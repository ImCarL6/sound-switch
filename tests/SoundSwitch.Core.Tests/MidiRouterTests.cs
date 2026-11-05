using SoundSwitch.Core.Config;
using SoundSwitch.Core.Routing;

namespace SoundSwitch.Core.Tests;

public class MidiRouterTests
{
    static AppConfig Config() => new()
    {
        Midi = new MidiConfig { InputName = "BK492", Channel = 1 },
        Devices = new(StringComparer.OrdinalIgnoreCase) { ["edifier"] = new() { Match = new() { Name = "EDIFIER" } } },
        Controls =
        [
            new() { Name = "Fader 1", Cc = 82, Type = ControlTypes.Fader, Action = ControlActions.DeviceVolume, Device = "edifier" },
            new() { Name = "Botão", Cc = 20, Type = ControlTypes.Button, Action = ControlActions.CycleDefaultDevice, TriggerValue = 127, DebounceMs = 250, Cycle = ["edifier"] },
        ],
    };

    static MidiMessage Cc(int cc, int value, int channel = 1) => new((byte)(0xB0 | (channel - 1)), (byte)cc, (byte)value);

    [Fact]
    public void Decodifica_formato_winmm()
    {
        var m = MidiMessage.FromWinMM(0x7F14B0);
        Assert.True(m.IsControlChange);
        Assert.Equal(1, m.Channel);
        Assert.Equal(20, m.Data1);
        Assert.Equal(127, m.Data2);
    }

    [Fact]
    public void Fader_gera_acao_com_valor()
    {
        var r = new MidiRouter(Config());
        var a = Assert.IsType<FaderMoved>(r.Route(Cc(82, 64)));
        Assert.Equal(64, a.Raw);
        Assert.Equal(0, a.ControlIndex);
    }

    [Fact]
    public void Valor_repetido_do_fader_eh_ignorado()
    {
        var r = new MidiRouter(Config());
        Assert.NotNull(r.Route(Cc(82, 64)));
        Assert.Null(r.Route(Cc(82, 64)));
        Assert.NotNull(r.Route(Cc(82, 65)));
    }

    [Fact]
    public void Ignora_outro_canal_cc_desconhecido_e_nao_cc()
    {
        var r = new MidiRouter(Config());
        Assert.Null(r.Route(Cc(82, 64, channel: 2)));
        Assert.Null(r.Route(Cc(83, 64)));
        Assert.Null(r.Route(new MidiMessage(0x90, 60, 100))); // note on
    }

    [Fact]
    public void Botao_dispara_no_127_e_ignora_soltura()
    {
        long now = 1000;
        var r = new MidiRouter(Config(), () => now);
        Assert.IsType<ButtonPressed>(r.Route(Cc(20, 127)));
        now += 120;
        Assert.Null(r.Route(Cc(20, 0)));
    }

    [Fact]
    public void Botao_tem_debounce()
    {
        long now = 1000;
        var r = new MidiRouter(Config(), () => now);
        Assert.NotNull(r.Route(Cc(20, 127)));
        now += 100;
        Assert.Null(r.Route(Cc(20, 127)));
        now += 400; // 500 ms depois do primeiro, como na medição
        Assert.NotNull(r.Route(Cc(20, 127)));
    }
}
