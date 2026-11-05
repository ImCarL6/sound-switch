namespace SoundSwitch.Core.Feedback;

public enum FeedbackKind
{
    /// <summary>Volume de um dispositivo (faders 1 e 3).</summary>
    DeviceVolume,
    /// <summary>Volume do app em foco (fader 2).</summary>
    FocusVolume,
    /// <summary>Troca do dispositivo padrão (botão).</summary>
    DeviceSwitch,
    /// <summary>Alvo indisponível: dispositivo desconectado, app sem áudio, função ainda não implementada.</summary>
    Unavailable,
    Error,
}

/// <summary>O que o overlay deve mostrar. Produzido pelo worker de áudio, consumido pela UI.</summary>
public sealed record FeedbackState(FeedbackKind Kind, string Label, int? Percent = null, bool Muted = false, string? Detail = null);
