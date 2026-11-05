using SoundSwitch.Core.Config;
using SoundSwitch.Core.Mapping;

namespace SoundSwitch.Core.Tests;

// Testes obrigatórios de docs/04-volume-e-calibracao.md
public class VolumeMapperTests
{
    static readonly CalibrationConfig Default = new() { Min = 0, Max = 127 };

    [Fact]
    public void Minimo_eh_zero_e_mudo()
    {
        Assert.Equal(new VolumeLevel(0, true), VolumeMapper.Map(0, Default));
    }

    [Fact]
    public void Maximo_eh_cem_sem_mute()
    {
        Assert.Equal(new VolumeLevel(100, false), VolumeMapper.Map(127, Default));
    }

    [Fact]
    public void Fora_da_calibracao_faz_clamp()
    {
        var cal = new CalibrationConfig { Min = 5, Max = 120 };
        Assert.Equal(new VolumeLevel(0, true), VolumeMapper.Map(2, cal));
        Assert.Equal(new VolumeLevel(100, false), VolumeMapper.Map(123, cal));
    }

    [Theory] // tabela de referência do doc 04
    [InlineData(1, 1)]
    [InlineData(13, 10)]
    [InlineData(32, 25)]
    [InlineData(64, 50)]
    [InlineData(95, 75)]
    [InlineData(102, 80)]
    [InlineData(114, 90)]
    [InlineData(126, 99)]
    public void Tabela_de_referencia(int raw, int percent)
    {
        var v = VolumeMapper.Map(raw, Default);
        Assert.Equal(percent, v.Percent);
        Assert.False(v.Mute);
    }

    [Fact]
    public void Monotonico_em_todo_o_curso()
    {
        for (int raw = 1; raw <= 127; raw++)
            Assert.True(VolumeMapper.Map(raw, Default).Percent >= VolumeMapper.Map(raw - 1, Default).Percent, $"raw {raw}");
    }

    [Fact]
    public void Linear_dentro_de_meio_ponto()
    {
        for (int raw = 0; raw <= 127; raw++)
        {
            double ideal = 100.0 * raw / 127;
            Assert.True(Math.Abs(VolumeMapper.Map(raw, Default).Percent - ideal) <= 0.5, $"raw {raw}");
        }
    }

    [Fact]
    public void Cem_por_cento_so_no_topo()
    {
        for (int raw = 0; raw < 127; raw++)
            Assert.True(VolumeMapper.Map(raw, Default).Percent < 100, $"raw {raw} já deu 100%");
    }

    [Fact]
    public void Linear_por_partes_com_ponto_do_meio()
    {
        // Fader que entrega 80 no meio físico do curso.
        var cal = new CalibrationConfig { Min = 0, Max = 127, Points = [[80, 50]] };
        Assert.Equal(50, VolumeMapper.Map(80, cal).Percent);
        Assert.Equal(25, VolumeMapper.Map(40, cal).Percent);
        Assert.Equal(74, VolumeMapper.Map(103, cal).Percent); // 50 + 23*50/47 = 74.47
        Assert.Equal(100, VolumeMapper.Map(127, cal).Percent);
        for (int raw = 1; raw <= 127; raw++)
            Assert.True(VolumeMapper.Map(raw, cal).Percent >= VolumeMapper.Map(raw - 1, cal).Percent);
    }

    [Fact]
    public void Scalar_eh_porcentagem_sobre_cem()
    {
        Assert.Equal(0.37f, new VolumeLevel(37, false).Scalar);
    }
}
