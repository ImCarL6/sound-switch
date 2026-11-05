using SoundSwitch.Core.Config;

namespace SoundSwitch.Core.Devices;

/// <summary>Endpoint de saída ativo, como o Windows o descreve.</summary>
/// <param name="Id">ID do endpoint, ex.: "{0.0.0.00000000}.{95fe9aea-…}".</param>
/// <param name="Name">Descrição do endpoint (PKEY_Device_DeviceDesc), ex.: "EDIFIER".</param>
/// <param name="Adapter">Nome do adaptador (PKEY_DeviceInterface_FriendlyName), ex.: "CS43131+SGM8262 HIFI Audio".</param>
/// <param name="FullName">Nome completo, ex.: "EDIFIER (CS43131+SGM8262 HIFI Audio)".</param>
public sealed record EndpointInfo(string Id, string Name, string Adapter, string FullName);

public enum MatchKind { KnownId, Name }

public sealed record DeviceMatch(EndpointInfo Endpoint, MatchKind Kind)
{
    /// <summary>Casou por nome com um ID que ainda não está em knownIds → vale gravar.</summary>
    public bool IsNewId => Kind == MatchKind.Name;
}

/// <summary>
/// Resolve um alvo do config para um endpoint ativo. IDs mudam neste PC (troca de porta USB,
/// driver, renomear a saída Realtek), então: primeiro knownIds, depois nome (+ adaptador).
/// Ver docs/03-arquitetura.md#identificação-de-dispositivos.
/// </summary>
public static class DeviceMatcher
{
    public static DeviceMatch? Resolve(DeviceTargetConfig target, IReadOnlyList<EndpointInfo> activeEndpoints)
    {
        foreach (var known in target.KnownIds)
        {
            var hit = activeEndpoints.FirstOrDefault(e => string.Equals(e.Id, known, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return new DeviceMatch(hit, MatchKind.KnownId);
        }

        var byName = activeEndpoints.Where(e => NameMatches(target.Match.Name, e)).ToList();
        if (byName.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(target.Match.Adapter))
        {
            var withAdapter = byName.FirstOrDefault(e => string.Equals(e.Adapter, target.Match.Adapter, StringComparison.OrdinalIgnoreCase));
            if (withAdapter is not null) return new DeviceMatch(withAdapter, MatchKind.Name);
        }

        return new DeviceMatch(byName[0], MatchKind.Name);
    }

    static bool NameMatches(string wanted, EndpointInfo e) =>
        string.Equals(e.Name, wanted, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(e.FullName, wanted, StringComparison.OrdinalIgnoreCase);

    /// <summary>Nome curto para o overlay: label do config, senão a descrição do endpoint.</summary>
    public static string DisplayLabel(DeviceTargetConfig target, EndpointInfo? endpoint) =>
        !string.IsNullOrWhiteSpace(target.Label) ? target.Label!
        : endpoint?.Name is { Length: > 0 } n ? n
        : target.Match.Name;
}
