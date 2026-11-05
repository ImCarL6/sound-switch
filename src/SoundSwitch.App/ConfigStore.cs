using System.IO;
using System.Reflection;
using System.Windows.Threading;
using SoundSwitch.Core.Config;
using SoundSwitch.Windows.Diagnostics;

namespace SoundSwitch.App;

/// <summary>
/// %APPDATA%\sound-switch\config.json: cria a partir do exemplo embutido, carrega, observa mudanças
/// (recarga a quente, RF6.4) e grava IDs aprendidos. Usar só na thread de UI.
/// </summary>
internal sealed class ConfigStore : IDisposable
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sound-switch");
    public static readonly string FilePath = Path.Combine(Dir, "config.json");
    public static readonly string LogDir = Path.Combine(Dir, "logs");

    readonly FileSystemWatcher watcher;
    readonly DispatcherTimer debounce;
    string? lastWrittenByUs;

    /// <summary>Config válido novo (ou null + erros quando o arquivo está inválido).</summary>
    public event Action<ConfigLoadResult>? Reloaded;

    public ConfigStore()
    {
        Directory.CreateDirectory(Dir);
        debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        debounce.Tick += (_, _) => { debounce.Stop(); Reloaded?.Invoke(Load()); };

        watcher = new FileSystemWatcher(Dir, "config.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        FileSystemEventHandler onChange = (_, _) => debounce.Dispatcher.BeginInvoke(() => { debounce.Stop(); debounce.Start(); });
        watcher.Changed += onChange;
        watcher.Created += onChange;
        watcher.Renamed += (s, e) => onChange(s, e);
    }

    public void StartWatching() => watcher.EnableRaisingEvents = true;

    public ConfigLoadResult Load()
    {
        if (!File.Exists(FilePath))
        {
            File.WriteAllText(FilePath, ReadExample());
            Log.Info($"Config: criado {FilePath} a partir do exemplo.");
        }

        string json;
        try { json = ReadShared(FilePath); }
        catch (IOException ex) { return new ConfigLoadResult(null, [$"Não consegui ler o config: {ex.Message}"]); }

        if (json == lastWrittenByUs) return new ConfigLoadResult(null, []); // eco da nossa própria gravação
        return ConfigLoader.Parse(json);
    }

    /// <summary>Acrescenta um ID aprendido em devices.&lt;key&gt;.knownIds sem disparar recarga.</summary>
    public void AddKnownId(string deviceKey, string id)
    {
        try
        {
            var updated = ConfigLoader.AddKnownId(ReadShared(FilePath), deviceKey, id);
            if (updated is null) return;
            lastWrittenByUs = updated;
            File.WriteAllText(FilePath, updated);
            Log.Info($"Config: \"{id}\" adicionado em devices.{deviceKey}.knownIds.");
        }
        catch (Exception ex)
        {
            Log.Warn($"Config: não consegui gravar o ID aprendido: {ex.Message}");
        }
    }

    static string ReadShared(string path)
    {
        // Editores às vezes seguram o arquivo por um instante ao salvar.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                return sr.ReadToEnd();
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }

    static string ReadExample()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("config.example.json")
            ?? throw new InvalidOperationException("config.example.json não está embutido.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public void Dispose()
    {
        watcher.Dispose();
        debounce.Stop();
    }
}
