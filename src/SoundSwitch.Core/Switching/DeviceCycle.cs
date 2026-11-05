namespace SoundSwitch.Core.Switching;

/// <summary>Um item do ciclo já resolvido: ID do endpoint ativo, ou null se desconectado.</summary>
public readonly record struct CycleEntry(string Key, string? EndpointId);

public enum CycleOutcome
{
    /// <summary>Trocar para <see cref="CycleResult.Index"/>.</summary>
    Switch,
    /// <summary>O padrão atual é o único item conectado: nada a trocar.</summary>
    OnlyOption,
    /// <summary>Nenhum item do ciclo está conectado.</summary>
    NoneAvailable,
}

public readonly record struct CycleResult(CycleOutcome Outcome, int Index);

/// <summary>
/// Escolhe o próximo dispositivo padrão (RF4): o próximo conectado depois do atual, na ordem do
/// config, dando a volta; se o padrão atual não está no ciclo, o primeiro conectado (RF4.7).
/// </summary>
public static class DeviceCycle
{
    public static CycleResult Next(IReadOnlyList<CycleEntry> cycle, string? currentDefaultId)
    {
        int current = -1;
        for (int i = 0; i < cycle.Count; i++)
            if (cycle[i].EndpointId is not null &&
                string.Equals(cycle[i].EndpointId, currentDefaultId, StringComparison.OrdinalIgnoreCase))
            { current = i; break; }

        if (current < 0)
        {
            for (int i = 0; i < cycle.Count; i++)
                if (cycle[i].EndpointId is not null) return new(CycleOutcome.Switch, i);
            return new(CycleOutcome.NoneAvailable, -1);
        }

        for (int step = 1; step < cycle.Count; step++)
        {
            int i = (current + step) % cycle.Count;
            if (cycle[i].EndpointId is not null) return new(CycleOutcome.Switch, i);
        }
        return new(CycleOutcome.OnlyOption, current);
    }
}
