using Microsoft.Win32;
using SoundSwitch.Windows.Diagnostics;

namespace SoundSwitch.App;

/// <summary>
/// Iniciar com o Windows (RF6.2) via HKCU\...\Run — sem admin. Builds de desenvolvimento
/// (bin\Debug, bin\Release) nunca se registram, para não apontar a inicialização para a pasta do código.
/// </summary>
internal static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "sound-switch";

    public static bool IsDevBuild(string exePath) =>
        exePath.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) ||
        exePath.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase);

    public static string? RegisteredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    public static bool IsEnabled => RegisteredCommand() is not null;

    /// <summary>Deixa o registro igual ao config. Retorna uma descrição do estado para a bandeja.</summary>
    public static string Apply(bool enable)
    {
        var exe = Environment.ProcessPath ?? "";
        var wanted = $"\"{exe}\"";
        var current = RegisteredCommand();

        if (IsDevBuild(exe))
        {
            Log.Info($"Inicialização: build de desenvolvimento, não mexo no registro (atual: {current ?? "nenhum"}).");
            return current is null ? "Iniciar com o Windows: desligado (build dev)" : "Iniciar com o Windows: ligado (outra cópia)";
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enable && !string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, wanted);
                Log.Info($"Inicialização: registrado {wanted}.");
            }
            else if (!enable && current is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("Inicialização: removido do registro.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Inicialização: não consegui alterar o registro", ex);
        }
        return enable ? "Iniciar com o Windows: ligado" : "Iniciar com o Windows: desligado";
    }
}
