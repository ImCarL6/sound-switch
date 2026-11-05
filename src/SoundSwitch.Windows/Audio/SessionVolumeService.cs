using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using SoundSwitch.Core.Focus;
using SoundSwitch.Core.Mapping;
using SoundSwitch.Windows.Diagnostics;
using SoundSwitch.Windows.Focus;

namespace SoundSwitch.Windows.Audio;

/// <summary>
/// Volume das sessões de áudio do app em foco, em todas as saídas ativas (RF3).
/// A lista de sessões do app é guardada em cache e refeita quando o foco muda, quando os
/// dispositivos mudam, ou a cada 500 ms durante o movimento do fader (pega sessões novas).
/// Uso exclusivo da thread do <see cref="VolumeEngine"/>.
/// </summary>
internal sealed class SessionVolumeService(EndpointRegistry registry) : IDisposable
{
    const long CacheMs = 500;

    readonly List<AudioSessionControl> cached = new();
    FocusedApp? cachedFor;
    long cachedAt;
    int cachedVersion = -1;

    public void Invalidate() => cachedFor = null;

    /// <summary>Aplica o volume. Retorna quantas sessões foram alteradas (0 = app sem áudio).</summary>
    public int Apply(FocusedApp app, VolumeLevel level)
    {
        try
        {
            return SetAll(SessionsOf(app), level);
        }
        catch (COMException ex)
        {
            // Sessão ou dispositivo sumiu no meio do caminho: refaz a lista e tenta uma vez.
            Log.Debug($"Sessões: COM falhou ({ex.HResult:X8}); refazendo a lista.");
            registry.Invalidate();
            Invalidate();
            return SetAll(SessionsOf(app), level);
        }
    }

    static int SetAll(IReadOnlyList<AudioSessionControl> sessions, VolumeLevel level)
    {
        int applied = 0;
        foreach (var s in sessions)
        {
            SimpleAudioVolume vol;
            try { vol = s.SimpleAudioVolume; }
            catch (NotSupportedException) { continue; }

            if (level.Mute)
            {
                vol.Volume = 0f;
                if (!vol.Mute) vol.Mute = true;
            }
            else
            {
                vol.Volume = level.Scalar;
                if (vol.Mute) vol.Mute = false;
            }
            applied++;
        }
        return applied;
    }

    IReadOnlyList<AudioSessionControl> SessionsOf(FocusedApp app)
    {
        long now = Environment.TickCount64;
        if (ReferenceEquals(cachedFor, app) && registry.Version == cachedVersion && now - cachedAt < CacheMs)
            return cached;

        Clear();
        var devices = registry.ActiveDevices; // pode recarregar e mudar a Version
        var parents = ProcessInfo.ParentMap();
        var paths = new Dictionary<int, string?>();
        var matchedPids = new SortedSet<int>();

        foreach (var device in devices)
        {
            SessionCollection list;
            try
            {
                var mgr = device.AudioSessionManager;
                mgr.RefreshSessions();
                list = mgr.Sessions;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                Log.Debug($"Sessões: não deu para listar em \"{device.FriendlyName}\": {ex.Message}");
                continue;
            }

            for (int i = 0; i < list.Count; i++)
            {
                var session = list[i];
                bool keep = false;
                try
                {
                    if (!session.IsSystemSoundsSession && session.State != AudioSessionState.AudioSessionStateExpired)
                    {
                        int pid = (int)session.GetProcessID;
                        if (!paths.TryGetValue(pid, out var path)) paths[pid] = path = ProcessInfo.ExePath(pid);
                        keep = SessionMatcher.Matches(app, new SessionProcess(pid, path), parents);
                        if (keep) matchedPids.Add(pid);
                    }
                }
                catch (COMException) { /* sessão encerrou enquanto líamos */ }

                if (keep) cached.Add(session);
                else session.Dispose();
            }
        }

        cachedFor = app;
        cachedAt = now;
        cachedVersion = registry.Version;
        Log.Debug($"Sessões de {app.DisplayName}: {cached.Count} (pids {string.Join(", ", matchedPids)})");
        return cached;
    }

    void Clear()
    {
        foreach (var s in cached) s.Dispose();
        cached.Clear();
    }

    public void Dispose() => Clear();
}
