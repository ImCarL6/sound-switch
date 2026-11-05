namespace SoundSwitch.Core.Focus;

/// <summary>App em primeiro plano, já resolvido (UWP → processo real).</summary>
/// <param name="Pid">Processo dono da janela em foco.</param>
/// <param name="ExePath">Caminho completo do executável; null se o Windows negou acesso.</param>
/// <param name="DisplayName">Nome amigável (descrição do arquivo, senão nome do exe).</param>
public sealed record FocusedApp(int Pid, string? ExePath, string DisplayName);

/// <summary>Processo dono de uma sessão de áudio.</summary>
public readonly record struct SessionProcess(int Pid, string? ExePath);

/// <summary>
/// Decide se uma sessão de áudio pertence ao app em foco (RF3.2, docs/03-arquitetura.md):
/// mesmo PID, mesmo executável (Chrome/Discord tocam áudio em processos filhos com o mesmo exe)
/// ou processo descendente do processo em foco.
/// </summary>
public static class SessionMatcher
{
    const int MaxDepth = 16;

    /// <param name="parentOf">PID → PID do pai (snapshot dos processos).</param>
    public static bool Matches(FocusedApp app, SessionProcess session, IReadOnlyDictionary<int, int> parentOf)
    {
        if (session.Pid <= 0) return false; // sons do sistema / sessão sem processo
        if (session.Pid == app.Pid) return true;

        if (app.ExePath is not null && session.ExePath is not null &&
            string.Equals(app.ExePath, session.ExePath, StringComparison.OrdinalIgnoreCase))
            return true;

        int pid = session.Pid;
        for (int depth = 0; depth < MaxDepth && parentOf.TryGetValue(pid, out var parent); depth++)
        {
            if (parent == app.Pid) return true;
            if (parent <= 4 || parent == pid) break; // System/Idle ou ciclo
            pid = parent;
        }
        return false;
    }
}
