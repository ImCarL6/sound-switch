namespace SoundSwitch.Core.Config;

/// <summary>Raiz do config.json (ver config/config.example.json e docs/03-arquitetura.md).</summary>
public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;
    public MidiConfig Midi { get; set; } = new();
    public Dictionary<string, DeviceTargetConfig> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ControlConfig> Controls { get; set; } = new();
    public OverlayConfig Overlay { get; set; } = new();
    public AppSettings App { get; set; } = new();
}

public sealed class MidiConfig
{
    /// <summary>Nome (ou parte do nome) da entrada MIDI. Comparação "contém", sem diferenciar maiúsculas.</summary>
    public string InputName { get; set; } = "BK492";

    /// <summary>Canal MIDI 1–16.</summary>
    public int Channel { get; set; } = 1;
}

public sealed class DeviceTargetConfig
{
    /// <summary>Nome curto mostrado no overlay. Se vazio, usa o nome do endpoint.</summary>
    public string? Label { get; set; }
    public DeviceMatchConfig Match { get; set; } = new();

    /// <summary>IDs de endpoint já vistos para este alvo. O app adiciona IDs novos sozinho.</summary>
    public List<string> KnownIds { get; set; } = new();
}

public sealed class DeviceMatchConfig
{
    /// <summary>Nome do endpoint, ex.: "EDIFIER" (parte antes dos parênteses no nome completo).</summary>
    public string Name { get; set; } = "";

    /// <summary>Nome do adaptador, ex.: "Realtek(R) Audio". Opcional.</summary>
    public string? Adapter { get; set; }
}

public static class ControlTypes
{
    public const string Fader = "fader";
    public const string Button = "button";
}

public static class ControlActions
{
    public const string DeviceVolume = "deviceVolume";
    public const string FocusVolume = "focusVolume";
    public const string CycleDefaultDevice = "cycleDefaultDevice";
}

public sealed class ControlConfig
{
    public string Name { get; set; } = "";
    public int Cc { get; set; }
    public string Type { get; set; } = ControlTypes.Fader;
    public string Action { get; set; } = ControlActions.DeviceVolume;

    // deviceVolume
    public string? Device { get; set; }

    // faders
    public CalibrationConfig Calibration { get; set; } = new();
    public int JitterFilter { get; set; }

    // botões
    public int TriggerValue { get; set; } = 127;
    public int DebounceMs { get; set; } = 250;

    // cycleDefaultDevice
    public List<string> Cycle { get; set; } = new();
    public List<string> Roles { get; set; } = new() { "console", "multimedia" };
}

public sealed class CalibrationConfig
{
    public int Min { get; set; }
    public int Max { get; set; } = 127;

    /// <summary>Pontos intermediários opcionais [raw, percent] para mapeamento linear por partes.</summary>
    public List<int[]> Points { get; set; } = new();
}

public sealed class OverlayConfig
{
    public bool Enabled { get; set; } = true;
    public string Position { get; set; } = "top-left";
    public int Margin { get; set; } = 16;
    public int HideAfterMs { get; set; } = 1500;
    public int DeviceSwitchHideAfterMs { get; set; } = 2000;
    public string Theme { get; set; } = "dark";
    public string Monitor { get; set; } = "primary";
}

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; } = true;
    public string LogLevel { get; set; } = "Information";
}
