using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace SoundSwitch.App;

/// <summary>Ícone da bandeja (RF6.1). Mínimo da Fase 1: status, abrir/recarregar config, logs, sair.</summary>
internal sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon icon;
    readonly Forms.ToolStripMenuItem midiStatus = new("MIDI: procurando…") { Enabled = false };
    readonly Forms.ToolStripMenuItem configStatus = new("Config: —") { Enabled = false };
    readonly Icon image;

    public event Action? ReloadRequested;
    public event Action? ExitRequested;

    public TrayIcon(string configPath, string logDir)
    {
        image = CreateIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("sound-switch") { Enabled = false, Font = new Font(Forms.Control.DefaultFont, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(midiStatus);
        menu.Items.Add(configStatus);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Abrir config", null, (_, _) => Open(configPath));
        menu.Items.Add("Recarregar config", null, (_, _) => ReloadRequested?.Invoke());
        menu.Items.Add("Abrir pasta de logs", null, (_, _) => Open(logDir));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitRequested?.Invoke());

        icon = new Forms.NotifyIcon { Icon = image, Text = "sound-switch", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => Open(configPath);
    }

    public void SetMidiStatus(bool connected, string? name)
    {
        midiStatus.Text = connected ? $"MIDI: {name} conectado" : "MIDI: desconectado (procurando…)";
        icon.Text = Trim(connected ? $"sound-switch — {name}" : "sound-switch — MIDI desconectado");
    }

    public void SetConfigStatus(string text) => configStatus.Text = Trim(text, 120);

    public void ShowError(string title, string text) =>
        icon.ShowBalloonTip(5000, title, Trim(text, 250), Forms.ToolTipIcon.Error);

    static string Trim(string s, int max = 63) => s.Length <= max ? s : s[..(max - 1)] + "…";

    static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { SoundSwitch.Windows.Diagnostics.Log.Warn($"Não consegui abrir {path}: {ex.Message}"); }
    }

    /// <summary>Três faders desenhados à mão: evita depender de arquivo .ico.</summary>
    static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var track = new Pen(Color.FromArgb(220, 230, 230, 230), 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var knob = new SolidBrush(Color.FromArgb(255, 76, 154, 255));
            int[] knobs = [20, 9, 15];
            for (int i = 0; i < 3; i++)
            {
                int x = 6 + i * 10;
                g.DrawLine(track, x, 4, x, 28);
                g.FillRectangle(knob, x - 4, knobs[i] - 3, 8, 6);
            }
        }
        var h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        image.Dispose();
    }
}
