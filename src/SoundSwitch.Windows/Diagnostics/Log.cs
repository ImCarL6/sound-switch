using System.Text;

namespace SoundSwitch.Windows.Diagnostics;

public enum LogLevel { Debug, Information, Warning, Error }

/// <summary>Log em arquivo, um por dia, mantendo os últimos 7. Thread-safe e sem dependências.</summary>
public static class Log
{
    static readonly object Gate = new();
    static string? dir;
    static LogLevel min = LogLevel.Information;

    public static string? Directory => dir;

    public static void Init(string directory, string level)
    {
        dir = directory;
        System.IO.Directory.CreateDirectory(directory);
        SetLevel(level);
        try
        {
            foreach (var old in new DirectoryInfo(directory).GetFiles("sound-switch-*.log")
                         .OrderByDescending(f => f.Name).Skip(7))
                old.Delete();
        }
        catch { /* limpeza é opcional */ }
    }

    public static void SetLevel(string level) =>
        min = Enum.TryParse<LogLevel>(level, ignoreCase: true, out var l) ? l : LogLevel.Information;

    public static void Debug(string msg) => Write(LogLevel.Debug, msg);
    public static void Info(string msg) => Write(LogLevel.Information, msg);
    public static void Warn(string msg) => Write(LogLevel.Warning, msg);
    public static void Error(string msg, Exception? ex = null) => Write(LogLevel.Error, ex is null ? msg : $"{msg}: {ex}");

    static void Write(LogLevel level, string msg)
    {
        if (level < min) return;
        var now = DateTime.Now;
        var tag = level switch { LogLevel.Debug => "DBG", LogLevel.Information => "INF", LogLevel.Warning => "WRN", _ => "ERR" };
        var line = $"{now:HH:mm:ss.fff} {tag} [{Environment.CurrentManagedThreadId,2}] {msg}{Environment.NewLine}";
        System.Diagnostics.Debug.Write(line);
        if (dir is null) return;
        lock (Gate)
        {
            try { File.AppendAllText(Path.Combine(dir, $"sound-switch-{now:yyyyMMdd}.log"), line, Encoding.UTF8); }
            catch { /* log nunca derruba o app */ }
        }
    }
}
