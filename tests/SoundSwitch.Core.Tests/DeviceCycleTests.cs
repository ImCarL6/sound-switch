using SoundSwitch.Core.Switching;

namespace SoundSwitch.Core.Tests;

public class DeviceCycleTests
{
    const string Edifier = "{edifier}", Fones = "{fones}", Asus = "{asus}";
    static readonly CycleEntry[] Both = [new("edifier", Edifier), new("fones", Fones)];

    [Fact]
    public void Alterna_entre_os_dois()
    {
        Assert.Equal(new CycleResult(CycleOutcome.Switch, 1), DeviceCycle.Next(Both, Edifier));
        Assert.Equal(new CycleResult(CycleOutcome.Switch, 0), DeviceCycle.Next(Both, Fones));
    }

    [Fact]
    public void Compara_id_sem_diferenciar_maiusculas()
    {
        Assert.Equal(1, DeviceCycle.Next(Both, Edifier.ToUpperInvariant()).Index);
    }

    [Fact]
    public void Padrao_fora_do_ciclo_vai_para_o_primeiro()
    {
        // ex.: alguém pôs o AI Noise-Canceling Speaker como padrão pelo Windows
        Assert.Equal(new CycleResult(CycleOutcome.Switch, 0), DeviceCycle.Next(Both, Asus));
        Assert.Equal(new CycleResult(CycleOutcome.Switch, 0), DeviceCycle.Next(Both, null));
    }

    [Fact]
    public void Pula_desconectado()
    {
        CycleEntry[] three = [new("a", "{a}"), new("b", null), new("c", "{c}")];
        Assert.Equal(2, DeviceCycle.Next(three, "{a}").Index);
        Assert.Equal(0, DeviceCycle.Next(three, "{c}").Index);
    }

    [Fact]
    public void Edifier_desplugado_com_fones_padrao_nao_troca()
    {
        CycleEntry[] semEdifier = [new("edifier", null), new("fones", Fones)];
        Assert.Equal(new CycleResult(CycleOutcome.OnlyOption, 1), DeviceCycle.Next(semEdifier, Fones));
    }

    [Fact]
    public void Edifier_desplugado_com_outro_padrao_forca_fones()
    {
        CycleEntry[] semEdifier = [new("edifier", null), new("fones", Fones)];
        Assert.Equal(new CycleResult(CycleOutcome.Switch, 1), DeviceCycle.Next(semEdifier, Asus));
    }

    [Fact]
    public void Nenhum_conectado()
    {
        CycleEntry[] none = [new("edifier", null), new("fones", null)];
        Assert.Equal(CycleOutcome.NoneAvailable, DeviceCycle.Next(none, Asus).Outcome);
    }

    [Fact]
    public void Ciclo_de_um_item()
    {
        CycleEntry[] one = [new("fones", Fones)];
        Assert.Equal(CycleOutcome.OnlyOption, DeviceCycle.Next(one, Fones).Outcome);
        Assert.Equal(new CycleResult(CycleOutcome.Switch, 0), DeviceCycle.Next(one, Edifier));
    }
}
