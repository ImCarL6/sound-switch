using SoundSwitch.Core.Config;

namespace SoundSwitch.Core.Tests;

public class ConfigLoaderTests
{
    static string ExamplePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "config", "config.example.json");
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException("config/config.example.json não encontrado");
    }

    [Fact]
    public void Exemplo_do_repositorio_eh_valido()
    {
        var r = ConfigLoader.Parse(File.ReadAllText(ExamplePath()));
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal("BK492", r.Config!.Midi.InputName);
        Assert.Equal(4, r.Config.Controls.Count);
        Assert.Equal([82, 83, 28, 20], r.Config.Controls.Select(c => c.Cc));
        Assert.True(r.Config.Devices.ContainsKey("EDIFIER")); // sem diferenciar maiúsculas
    }

    [Fact]
    public void Json_quebrado_vira_erro_legivel()
    {
        var r = ConfigLoader.Parse("{ \"midi\": ");
        Assert.False(r.Ok);
        Assert.Contains("JSON inválido", r.Errors[0]);
    }

    [Fact]
    public void Aceita_comentarios_e_virgula_sobrando()
    {
        var r = ConfigLoader.Parse("""
            {
              // comentário
              "midi": { "inputName": "BK492", "channel": 1, },
            }
            """);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
    }

    [Fact]
    public void Detecta_erros_de_validacao()
    {
        var r = ConfigLoader.Parse("""
            {
              "midi": { "inputName": "BK492", "channel": 17 },
              "devices": { "a": { "match": { "name": "A" } } },
              "controls": [
                { "name": "f1", "cc": 10, "type": "fader", "action": "deviceVolume", "device": "naoexiste" },
                { "name": "f2", "cc": 10, "type": "fader", "action": "focusVolume", "calibration": { "min": 100, "max": 50 } },
                { "name": "b",  "cc": 20, "type": "button", "action": "cycleDefaultDevice", "cycle": ["a", "x"], "roles": ["foo"] }
              ]
            }
            """);
        Assert.False(r.Ok);
        var all = string.Join("\n", r.Errors);
        Assert.Contains("midi.channel", all);
        Assert.Contains("\"naoexiste\" não existe", all);
        Assert.Contains("cc 10 está repetido", all);
        Assert.Contains("min (100) deve ser menor", all);
        Assert.Contains("\"x\" do ciclo não existe", all);
        Assert.Contains("role \"foo\"", all);
    }

    [Fact]
    public void SetStartWithWindows_altera_e_preserva_o_resto()
    {
        var json = """{ "midi": { "inputName": "BK492" }, "app": { "startWithWindows": true, "logLevel": "Debug" } }""";
        var off = ConfigLoader.SetStartWithWindows(json, false);
        Assert.NotNull(off);
        var cfg = ConfigLoader.Parse(off!).Config!;
        Assert.False(cfg.App.StartWithWindows);
        Assert.Equal("Debug", cfg.App.LogLevel);
        Assert.Equal("BK492", cfg.Midi.InputName);
        Assert.Null(ConfigLoader.SetStartWithWindows(off!, false)); // sem mudança
        Assert.True(ConfigLoader.Parse(ConfigLoader.SetStartWithWindows("{}", true)!).Config!.App.StartWithWindows);
    }

    [Fact]
    public void AddKnownId_acrescenta_uma_vez()
    {
        var json = """{ "devices": { "edifier": { "match": { "name": "EDIFIER" }, "knownIds": ["a"] } } }""";
        var updated = ConfigLoader.AddKnownId(json, "EDIFIER", "b");
        Assert.NotNull(updated);
        var cfg = ConfigLoader.Parse(updated!).Config!;
        Assert.Equal(["a", "b"], cfg.Devices["edifier"].KnownIds);
        Assert.Null(ConfigLoader.AddKnownId(updated!, "edifier", "B")); // já existe
    }
}
