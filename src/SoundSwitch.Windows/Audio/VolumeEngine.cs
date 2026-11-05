using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SoundSwitch.Core.Config;
using SoundSwitch.Core.Devices;
using SoundSwitch.Core.Feedback;
using SoundSwitch.Core.Focus;
using SoundSwitch.Core.Mapping;
using SoundSwitch.Core.Routing;
using SoundSwitch.Core.Switching;
using SoundSwitch.Windows.Diagnostics;

namespace SoundSwitch.Windows.Audio;

/// <summary>
/// Worker de áudio: uma thread dedicada, dona de todos os objetos COM (docs/03-arquitetura.md).
/// O callback MIDI só chama <see cref="Post"/>, que grava o último valor de cada fader num "slot"
/// e acorda a thread. Se chegarem 50 valores enquanto um está sendo aplicado, os intermediários
/// são pulados, mas o último sempre é aplicado (RNF4).
/// </summary>
public sealed class VolumeEngine : IDisposable
{
    /// <summary>Contexto passado nas chamadas de volume, para reconhecer notificações geradas por nós.</summary>
    public static readonly Guid EventContext = new("5b0d3c2e-7a43-4f0e-9a51-3f2f5c1d6a77");

    sealed class State(AppConfig config)
    {
        public readonly AppConfig Config = config;
        public readonly int[] Slots = Enumerable.Repeat(-1, config.Controls.Count).ToArray();
    }

    readonly Thread thread;
    readonly AutoResetEvent signal = new(false);
    readonly ConcurrentQueue<Action> work = new();
    readonly ConcurrentQueue<ButtonPressed> buttons = new();
    volatile State? state;
    volatile bool stopping;
    EndpointRegistry? registry;
    SessionVolumeService? sessions;
    volatile FocusedApp? focused;

    /// <summary>Feedback para o overlay. Chamado na thread do worker: não bloquear.</summary>
    public event Action<FeedbackState>? Feedback;

    /// <summary>(chave do dispositivo, ID novo) — o app grava em knownIds no config.json.</summary>
    public event Action<string, string>? KnownIdLearned;

    public VolumeEngine()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "VolumeEngine" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    public void ApplyConfig(AppConfig config)
    {
        state = new State(config);
        work.Enqueue(() => LogResolution(config));
        signal.Set();
    }

    /// <summary>Diagnóstico: registra para qual endpoint cada alvo do config resolve agora.</summary>
    void LogResolution(AppConfig cfg)
    {
        foreach (var (key, target) in cfg.Devices)
        {
            var m = ResolveAndLearn(key, target);
            Log.Info(m is null
                ? $"Áudio: \"{key}\" → DESCONECTADO (procurando \"{target.Match.Name}\")"
                : $"Áudio: \"{key}\" → {m.Endpoint.FullName} [{(m.Kind == MatchKind.KnownId ? "por ID" : "por nome, ID novo")}]");
        }
    }

    /// <summary>Força recarregar a lista de dispositivos (ex.: volta da suspensão).</summary>
    public void InvalidateDevices()
    {
        work.Enqueue(() => registry?.Invalidate());
        signal.Set();
    }

    /// <summary>Chamado no callback MIDI. Não bloqueia, não aloca para faders.</summary>
    public void Post(RoutedAction action)
    {
        var s = state;
        if (s is null) return;
        switch (action)
        {
            case FaderMoved f when f.ControlIndex < s.Slots.Length:
                Volatile.Write(ref s.Slots[f.ControlIndex], f.Raw);
                break;
            case ButtonPressed b:
                buttons.Enqueue(b);
                break;
            default:
                return;
        }
        signal.Set();
    }

    void Run()
    {
        try
        {
            registry = new EndpointRegistry();
            sessions = new SessionVolumeService(registry);
        }
        catch (Exception ex)
        {
            Log.Error("Áudio: falha ao iniciar MMDeviceEnumerator", ex);
            Feedback?.Invoke(new FeedbackState(FeedbackKind.Error, "Erro de áudio", Detail: ex.Message));
            return;
        }

        while (!stopping)
        {
            signal.WaitOne();
            if (stopping) break;

            while (work.TryDequeue(out var job)) Safe("tarefa", job);

            var s = state;
            if (s is null) continue;

            for (int i = 0; i < s.Slots.Length; i++)
            {
                int raw = Interlocked.Exchange(ref s.Slots[i], -1);
                if (raw >= 0) Safe($"controle \"{s.Config.Controls[i].Name}\"", () => ApplyFader(s.Config, s.Config.Controls[i], raw));
            }

            while (buttons.TryDequeue(out var b)) Safe($"botão \"{b.Control.Name}\"", () => ApplyButton(s.Config, b.Control));
        }

        sessions.Dispose();
        registry.Dispose();
    }

    void ApplyFader(AppConfig cfg, ControlConfig control, int raw)
    {
        var level = VolumeMapper.Map(raw, control.Calibration);
        switch (control.Action)
        {
            case ControlActions.DeviceVolume:
                ApplyDeviceVolume(cfg, control, raw, level);
                break;
            case ControlActions.FocusVolume:
                ApplyFocusVolume(raw, level);
                break;
        }
    }

    void ApplyFocusVolume(int raw, VolumeLevel level)
    {
        var app = focused;
        if (app is null)
        {
            Feedback?.Invoke(new FeedbackState(FeedbackKind.Unavailable, "App em foco", Detail: "nenhuma janela em foco"));
            return;
        }

        int count = sessions!.Apply(app, level);
        if (count == 0)
        {
            Feedback?.Invoke(new FeedbackState(FeedbackKind.Unavailable, app.DisplayName, Detail: "sem áudio", IconPath: app.ExePath));
            return;
        }

        Log.Debug($"Volume foco {app.DisplayName}: raw {raw} → {level.Percent}%{(level.Mute ? " (mudo)" : "")} em {count} sessão(ões)");
        Feedback?.Invoke(new FeedbackState(FeedbackKind.FocusVolume, app.DisplayName, level.Percent, level.Mute, IconPath: app.ExePath));
    }

    /// <summary>Chamado pela UI quando o app em foco muda. Não aplica volume (RF3.5).</summary>
    public void SetFocusedApp(FocusedApp app)
    {
        focused = app;
        work.Enqueue(() => sessions?.Invalidate());
        signal.Set();
    }

    void ApplyDeviceVolume(AppConfig cfg, ControlConfig control, int raw, VolumeLevel level)
    {
        var key = control.Device!;
        var target = cfg.Devices[key];
        var match = ResolveAndLearn(key, target);
        if (match is null)
        {
            Feedback?.Invoke(new FeedbackState(FeedbackKind.Unavailable, DeviceMatcher.DisplayLabel(target, null), Detail: "desconectado"));
            return;
        }

        var device = registry!.Get(match.Endpoint.Id);
        if (device is null) { registry.Invalidate(); return; }

        try
        {
            SetEndpointVolume(device, level);
        }
        catch (COMException ex)
        {
            // Endpoint pode ter sumido entre a notificação e a chamada: recarrega e tenta uma vez.
            Log.Warn($"Áudio: COM falhou em \"{match.Endpoint.FullName}\" ({ex.HResult:X8}); recarregando dispositivos.");
            registry.Invalidate();
            var again = DeviceMatcher.Resolve(target, registry.Active);
            var dev2 = again is null ? null : registry.Get(again.Endpoint.Id);
            if (dev2 is null)
            {
                Feedback?.Invoke(new FeedbackState(FeedbackKind.Unavailable, DeviceMatcher.DisplayLabel(target, null), Detail: "desconectado"));
                return;
            }
            SetEndpointVolume(dev2, level);
        }

        Log.Debug($"Volume {key}: raw {raw} → {level.Percent}%{(level.Mute ? " (mudo)" : "")}");
        Feedback?.Invoke(new FeedbackState(FeedbackKind.DeviceVolume, DeviceMatcher.DisplayLabel(target, match.Endpoint), level.Percent, level.Mute));
    }

    /// <summary>Resolve o alvo; se casou por nome com ID novo, guarda o ID (memória + config.json).</summary>
    DeviceMatch? ResolveAndLearn(string key, DeviceTargetConfig target)
    {
        var match = DeviceMatcher.Resolve(target, registry!.Active);
        if (match is { IsNewId: true })
        {
            target.KnownIds.Add(match.Endpoint.Id); // em memória: na próxima já casa por ID
            Log.Info($"Áudio: \"{key}\" resolvido por nome para um ID novo {match.Endpoint.Id}.");
            KnownIdLearned?.Invoke(key, match.Endpoint.Id);
        }
        return match;
    }

    static void SetEndpointVolume(NAudio.CoreAudioApi.MMDevice device, VolumeLevel level)
    {
        var vol = device.AudioEndpointVolume;
        vol.NotificationGuid = EventContext;
        if (level.Mute)
        {
            vol.MasterVolumeLevelScalar = 0f;
            if (!vol.Mute) vol.Mute = true;
        }
        else
        {
            // Volume antes de desmutar: evita um "estalo" no volume antigo.
            vol.MasterVolumeLevelScalar = level.Scalar;
            if (vol.Mute) vol.Mute = false;
        }
    }

    void ApplyButton(AppConfig cfg, ControlConfig control)
    {
        if (control.Action != ControlActions.CycleDefaultDevice) return;

        var entries = new List<CycleEntry>();
        var labels = new List<string>();
        foreach (var key in control.Cycle)
        {
            var target = cfg.Devices[key];
            var match = ResolveAndLearn(key, target);
            entries.Add(new CycleEntry(key, match?.Endpoint.Id));
            labels.Add(DeviceMatcher.DisplayLabel(target, match?.Endpoint));
        }

        string? current = registry!.DefaultRenderId();
        var result = DeviceCycle.Next(entries, current);

        switch (result.Outcome)
        {
            case CycleOutcome.NoneAvailable:
                Log.Warn("Troca de saída: nenhum dispositivo do ciclo está conectado.");
                Feedback?.Invoke(new FeedbackState(FeedbackKind.Unavailable, "Troca de saída", Detail: "nenhuma saída do ciclo conectada"));
                return;

            case CycleOutcome.OnlyOption:
                Log.Info($"Troca de saída: \"{labels[result.Index]}\" é a única saída do ciclo conectada.");
                Feedback?.Invoke(new FeedbackState(FeedbackKind.DeviceSwitch, $"Saída: {labels[result.Index]}",
                    Detail: "única saída disponível"));
                return;
        }

        var roles = control.Roles.Select(DefaultDeviceService.ParseRole).ToList();
        DefaultDeviceService.SetDefault(entries[result.Index].EndpointId!, roles);
        sessions?.Invalidate(); // apps tendem a recriar as sessões no dispositivo novo
        Log.Info($"Troca de saída: padrão ({string.Join("+", control.Roles)}) → \"{labels[result.Index]}\".");

        var list = string.Join("     ", entries.Select((e, i) =>
            $"{(i == result.Index ? "●" : "○")} {labels[i]}{(e.EndpointId is null ? " (desconectado)" : "")}"));
        Feedback?.Invoke(new FeedbackState(FeedbackKind.DeviceSwitch, $"Saída: {labels[result.Index]}", Detail: list));
    }

    static void Safe(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { Log.Error($"Áudio: erro ao aplicar {what}", ex); }
    }

    public void Dispose()
    {
        stopping = true;
        signal.Set();
        thread.Join(TimeSpan.FromSeconds(2));
        signal.Dispose();
    }
}
