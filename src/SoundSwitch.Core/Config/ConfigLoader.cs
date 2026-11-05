using System.Text.Json;
using System.Text.Json.Nodes;

namespace SoundSwitch.Core.Config;

public sealed record ConfigLoadResult(AppConfig? Config, IReadOnlyList<string> Errors)
{
    public bool Ok => Config is not null && Errors.Count == 0;
}

/// <summary>Lê e valida o config.json. Erros são mensagens em pt-BR prontas para mostrar ao usuário.</summary>
public static class ConfigLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public static ConfigLoadResult Parse(string json)
    {
        AppConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is long l ? $" (linha {l + 1})" : "";
            return new(null, [$"JSON inválido{where}: {ex.Message}"]);
        }
        if (cfg is null) return new(null, ["Config vazio."]);

        // O dicionário desserializado não herda o comparer; recria sem diferenciar maiúsculas.
        cfg.Devices = new Dictionary<string, DeviceTargetConfig>(cfg.Devices, StringComparer.OrdinalIgnoreCase);

        var errors = Validate(cfg);
        return new(errors.Count == 0 ? cfg : null, errors);
    }

    public static List<string> Validate(AppConfig cfg)
    {
        var e = new List<string>();

        if (string.IsNullOrWhiteSpace(cfg.Midi.InputName)) e.Add("midi.inputName está vazio.");
        if (cfg.Midi.Channel is < 1 or > 16) e.Add($"midi.channel deve estar entre 1 e 16 (atual: {cfg.Midi.Channel}).");

        foreach (var (key, dev) in cfg.Devices)
            if (string.IsNullOrWhiteSpace(dev.Match.Name))
                e.Add($"devices.{key}.match.name está vazio.");

        var seenCc = new HashSet<int>();
        for (int i = 0; i < cfg.Controls.Count; i++)
        {
            var c = cfg.Controls[i];
            var id = string.IsNullOrWhiteSpace(c.Name) ? $"controls[{i}]" : $"controle \"{c.Name}\"";

            if (c.Cc is < 0 or > 127) e.Add($"{id}: cc deve estar entre 0 e 127 (atual: {c.Cc}).");
            else if (!seenCc.Add(c.Cc)) e.Add($"{id}: cc {c.Cc} está repetido em outro controle.");

            switch (c.Type)
            {
                case ControlTypes.Fader:
                    if (c.Action is not (ControlActions.DeviceVolume or ControlActions.FocusVolume))
                        e.Add($"{id}: fader só aceita action \"deviceVolume\" ou \"focusVolume\" (atual: \"{c.Action}\").");
                    ValidateCalibration(c.Calibration, id, e);
                    if (c.JitterFilter is < 0 or > 3) e.Add($"{id}: jitterFilter deve estar entre 0 e 3.");
                    break;
                case ControlTypes.Button:
                    if (c.Action != ControlActions.CycleDefaultDevice)
                        e.Add($"{id}: botão só aceita action \"cycleDefaultDevice\" (atual: \"{c.Action}\").");
                    if (c.TriggerValue is < 0 or > 127) e.Add($"{id}: triggerValue deve estar entre 0 e 127.");
                    if (c.DebounceMs is < 0 or > 5000) e.Add($"{id}: debounceMs deve estar entre 0 e 5000.");
                    break;
                default:
                    e.Add($"{id}: type deve ser \"fader\" ou \"button\" (atual: \"{c.Type}\").");
                    break;
            }

            if (c.Action == ControlActions.DeviceVolume)
            {
                if (string.IsNullOrWhiteSpace(c.Device)) e.Add($"{id}: deviceVolume precisa de \"device\".");
                else if (!cfg.Devices.ContainsKey(c.Device)) e.Add($"{id}: dispositivo \"{c.Device}\" não existe em \"devices\".");
            }

            if (c.Action == ControlActions.CycleDefaultDevice)
            {
                if (c.Cycle.Count == 0) e.Add($"{id}: \"cycle\" precisa de pelo menos um dispositivo.");
                foreach (var d in c.Cycle)
                    if (!cfg.Devices.ContainsKey(d)) e.Add($"{id}: dispositivo \"{d}\" do ciclo não existe em \"devices\".");
                foreach (var r in c.Roles)
                    if (r is not ("console" or "multimedia" or "communications"))
                        e.Add($"{id}: role \"{r}\" inválido (use console, multimedia ou communications).");
            }
        }

        return e;
    }

    static void ValidateCalibration(CalibrationConfig cal, string id, List<string> e)
    {
        if (cal.Min is < 0 or > 127 || cal.Max is < 0 or > 127)
            e.Add($"{id}: calibration.min/max devem estar entre 0 e 127.");
        if (cal.Min >= cal.Max)
            e.Add($"{id}: calibration.min ({cal.Min}) deve ser menor que max ({cal.Max}).");

        int lastRaw = cal.Min, lastPct = 0;
        foreach (var p in cal.Points)
        {
            if (p is not { Length: 2 }) { e.Add($"{id}: cada ponto de calibration.points deve ser [raw, percent]."); return; }
            if (p[0] <= lastRaw || p[0] >= cal.Max || p[1] < lastPct || p[1] > 100)
            {
                e.Add($"{id}: calibration.points deve ser crescente, com raw entre min e max e percent entre 0 e 100.");
                return;
            }
            (lastRaw, lastPct) = (p[0], p[1]);
        }
    }

    /// <summary>
    /// Acrescenta um ID em devices.&lt;key&gt;.knownIds no texto JSON, preservando o resto do arquivo
    /// (exceto comentários, que o JsonNode não preserva). Retorna null se o ID já estava lá.
    /// </summary>
    public static string? AddKnownId(string json, string deviceKey, string endpointId)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject;
        if (root?["devices"] is not JsonObject devices) return null;

        var dev = devices.FirstOrDefault(kv => string.Equals(kv.Key, deviceKey, StringComparison.OrdinalIgnoreCase)).Value as JsonObject;
        if (dev is null) return null;

        if (dev["knownIds"] is not JsonArray ids) dev["knownIds"] = ids = new JsonArray();
        if (ids.Any(n => string.Equals(n?.GetValue<string>(), endpointId, StringComparison.OrdinalIgnoreCase))) return null;
        ids.Add(endpointId);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
