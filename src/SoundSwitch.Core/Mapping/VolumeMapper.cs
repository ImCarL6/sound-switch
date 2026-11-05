using SoundSwitch.Core.Config;

namespace SoundSwitch.Core.Mapping;

/// <summary>Volume já pronto para aplicar: porcentagem inteira (0–100) e mute.</summary>
public readonly record struct VolumeLevel(int Percent, bool Mute)
{
    /// <summary>Valor para SetMasterVolumeLevelScalar / SimpleAudioVolume (0.0–1.0).</summary>
    public float Scalar => Percent / 100f;
}

/// <summary>
/// Fader (raw 0–127) → volume. Especificação em docs/04-volume-e-calibracao.md:
/// linear sobre a faixa calibrada, arredondado para inteiro, min = 0% + mute, max = 100%.
/// </summary>
public static class VolumeMapper
{
    public static VolumeLevel Map(int raw, CalibrationConfig cal)
    {
        if (raw <= cal.Min) return new VolumeLevel(0, true);
        if (raw >= cal.Max) return new VolumeLevel(100, false);

        // Pontos de controle: (min,0) … pontos intermediários … (max,100)
        int x0 = cal.Min, y0 = 0;
        foreach (var p in cal.Points)
        {
            if (raw <= p[0]) return Make(Lerp(raw, x0, y0, p[0], p[1]));
            (x0, y0) = (p[0], p[1]);
        }
        return Make(Lerp(raw, x0, y0, cal.Max, 100));
    }

    static double Lerp(int raw, int x0, int y0, int x1, int y1) =>
        y0 + (double)(raw - x0) * (y1 - y0) / (x1 - x0);

    static VolumeLevel Make(double percent)
    {
        int p = (int)Math.Round(percent, MidpointRounding.AwayFromZero);
        p = Math.Clamp(p, 0, 100);
        // Dentro da faixa (raw > min) nunca muta: um passo acima do mínimo já é som.
        return new VolumeLevel(p, false);
    }
}
