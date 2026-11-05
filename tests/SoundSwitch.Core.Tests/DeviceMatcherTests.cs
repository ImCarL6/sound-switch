using SoundSwitch.Core.Config;
using SoundSwitch.Core.Devices;

namespace SoundSwitch.Core.Tests;

public class DeviceMatcherTests
{
    // Estado real do PC em 05/11/2026 (docs/01-analise-setup-atual.md)
    static readonly EndpointInfo Edifier = new("{0.0.0.00000000}.{95fe9aea-8956-4280-9634-89675f289bec}", "EDIFIER", "CS43131+SGM8262 HIFI Audio", "EDIFIER (CS43131+SGM8262 HIFI Audio)");
    static readonly EndpointInfo Fones = new("{0.0.0.00000000}.{079f7cf6-1c5d-437f-bf1a-efb781a24f05}", "Fones de ouvido", "Realtek(R) Audio", "Fones de ouvido (Realtek(R) Audio)");
    static readonly EndpointInfo Asus = new("{0.0.0.00000000}.{8a7d8427-f32c-41df-863e-eac38400aebf}", "AI Noise-Canceling Speaker", "ASUS Utility", "AI Noise-Canceling Speaker (ASUS Utility)");
    static readonly IReadOnlyList<EndpointInfo> Active = [Asus, Edifier, Fones];

    [Fact]
    public void Prefere_known_id()
    {
        var t = new DeviceTargetConfig { Match = new() { Name = "outro nome" }, KnownIds = ["antigo", Edifier.Id.ToUpperInvariant()] };
        var m = DeviceMatcher.Resolve(t, Active);
        Assert.Equal(Edifier, m!.Endpoint);
        Assert.Equal(MatchKind.KnownId, m.Kind);
        Assert.False(m.IsNewId);
    }

    [Fact]
    public void Id_mudou_casa_por_nome_e_marca_id_novo()
    {
        // IDs que o SoundSwitch tinha guardado e que não existem mais
        var t = new DeviceTargetConfig { Match = new() { Name = "EDIFIER" }, KnownIds = ["{0.0.0.00000000}.{984f57de-0f74-417d-a47a-903df43b4807}"] };
        var m = DeviceMatcher.Resolve(t, Active);
        Assert.Equal(Edifier, m!.Endpoint);
        Assert.True(m.IsNewId);
    }

    [Fact]
    public void Aceita_nome_completo()
    {
        var t = new DeviceTargetConfig { Match = new() { Name = "fones de ouvido (realtek(r) audio)" } };
        Assert.Equal(Fones, DeviceMatcher.Resolve(t, Active)!.Endpoint);
    }

    [Fact]
    public void Adaptador_desempata()
    {
        var outroFones = new EndpointInfo("{x}", "Fones de ouvido", "USB Audio CODEC", "Fones de ouvido (USB Audio CODEC)");
        var t = new DeviceTargetConfig { Match = new() { Name = "Fones de ouvido", Adapter = "Realtek(R) Audio" } };
        Assert.Equal(Fones, DeviceMatcher.Resolve(t, [outroFones, Fones])!.Endpoint);
    }

    [Fact]
    public void Desconectado_retorna_null()
    {
        var t = new DeviceTargetConfig { Match = new() { Name = "EDIFIER" } };
        Assert.Null(DeviceMatcher.Resolve(t, [Fones, Asus]));
    }

    [Fact]
    public void Label_do_config_tem_prioridade()
    {
        Assert.Equal("Caixas", DeviceMatcher.DisplayLabel(new() { Label = "Caixas", Match = new() { Name = "EDIFIER" } }, Edifier));
        Assert.Equal("EDIFIER", DeviceMatcher.DisplayLabel(new() { Match = new() { Name = "x" } }, Edifier));
        Assert.Equal("x", DeviceMatcher.DisplayLabel(new() { Match = new() { Name = "x" } }, null));
    }
}
