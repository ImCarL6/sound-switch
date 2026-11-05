using System.Windows;
using Microsoft.Win32;
using SoundSwitch.Core.Config;
using SoundSwitch.Core.Routing;
using SoundSwitch.Windows.Audio;
using SoundSwitch.Windows.Diagnostics;
using SoundSwitch.Windows.Focus;
using SoundSwitch.Windows.Midi;

namespace SoundSwitch.App;

/// <summary>
/// Monta as peças: MIDI → MidiRouter → VolumeEngine → OverlayWindow. Sem janela principal;
/// vive na bandeja. Ver docs/03-arquitetura.md.
/// </summary>
public partial class App : Application
{
    Mutex? singleInstance;
    ConfigStore? store;
    TrayIcon? tray;
    OverlayWindow? overlay;
    VolumeEngine? engine;
    MidiInputService? midi;
    ForegroundTracker? focus;
    volatile MidiRouter? router;
    AppConfig? current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        singleInstance = new Mutex(true, @"Local\sound-switch-midi", out bool first);
        if (!first)
        {
            MessageBox.Show("O sound-switch já está rodando (veja o ícone na bandeja).", "sound-switch",
                MessageBoxButton.OK, MessageBoxImage.Information);
            singleInstance = null;
            Shutdown();
            return;
        }

        Log.Init(ConfigStore.LogDir, "Information");
        Log.Info($"==== sound-switch iniciando (pid {Environment.ProcessId}) ====");
        DispatcherUnhandledException += (_, ex) => { Log.Error("Exceção na UI", ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error("Exceção não tratada", ex.ExceptionObject as Exception);

        store = new ConfigStore();
        tray = new TrayIcon(ConfigStore.FilePath, ConfigStore.LogDir);
        tray.ExitRequested += () => Shutdown();
        tray.ReloadRequested += () => OnConfig(store.Load(), manual: true);
        tray.AutoStartToggled += enabled => store.SetStartWithWindows(enabled);

        overlay = new OverlayWindow();

        engine = new VolumeEngine();
        engine.Feedback += s => overlay.Push(s);
        engine.KnownIdLearned += (key, id) => Dispatcher.BeginInvoke(() => store.AddKnownId(key, id));

        // Precisa da thread de UI (loop de mensagens) para receber os eventos de foco.
        focus = new ForegroundTracker();
        focus.Changed += app => engine.SetFocusedApp(app);
        if (focus.Current is { } startApp) engine.SetFocusedApp(startApp);

        var initial = store.Load();
        OnConfig(initial, manual: false);
        store.Reloaded += r => OnConfig(r, manual: false);
        store.StartWatching();

        midi = new MidiInputService(current?.Midi.InputName ?? "BK492");
        midi.ConnectionChanged += (connected, name) => Dispatcher.BeginInvoke(() => tray?.SetMidiStatus(connected, name));
        midi.MessageReceived += msg =>
        {
            // Thread do driver MIDI: só roteia e enfileira.
            var action = router?.Route(msg);
            if (action is not null) engine.Post(action);
        };
        midi.Start();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    void OnConfig(ConfigLoadResult result, bool manual)
    {
        if (result.Config is null)
        {
            if (result.Errors.Count == 0) return; // eco da nossa própria gravação
            var msg = string.Join("\n", result.Errors);
            Log.Warn("Config inválido, mantendo o anterior:\n" + msg);
            tray?.SetConfigStatus(current is null ? "Config: INVÁLIDO" : "Config: inválido (usando o anterior)");
            tray?.ShowError("Config inválido", msg);
            return;
        }

        current = result.Config;
        Log.SetLevel(current.App.LogLevel);
        router = new MidiRouter(current);
        engine?.ApplyConfig(current);
        overlay?.Configure(current.Overlay);
        midi?.SetInputName(current.Midi.InputName);
        tray?.SetConfigStatus($"Config: ok ({current.Controls.Count} controles)");
        tray?.SetAutoStart(current.App.StartWithWindows, AutoStart.Apply(current.App.StartWithWindows));
        Log.Info($"Config {(manual ? "recarregado manualmente" : "carregado")}: " +
                 string.Join(", ", current.Controls.Select(c => $"CC{c.Cc}→{c.Action}{(c.Device is null ? "" : ":" + c.Device)}")));
    }

    void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        Log.Info("Sistema voltou da suspensão: reabrindo MIDI e recarregando dispositivos.");
        midi?.Reopen();
        engine?.InvalidateDevices();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        midi?.Dispose();
        focus?.Dispose();
        engine?.Dispose();
        store?.Dispose();
        tray?.Dispose();
        if (singleInstance is not null)
        {
            singleInstance.ReleaseMutex();
            singleInstance.Dispose();
        }
        Log.Info("==== sound-switch encerrado ====");
        base.OnExit(e);
    }
}
