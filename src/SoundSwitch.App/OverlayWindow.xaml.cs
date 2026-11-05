using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SoundSwitch.Core.Config;
using SoundSwitch.Core.Feedback;

namespace SoundSwitch.App;

/// <summary>
/// Overlay de feedback (docs/05-overlay.md). Nunca recebe foco (WS_EX_NOACTIVATE) e deixa os cliques
/// passarem (WS_EX_TRANSPARENT). <see cref="Push"/> pode ser chamado de qualquer thread: só o último
/// estado é desenhado, no máximo uma vez por frame.
/// </summary>
public partial class OverlayWindow : Window
{
    const string GlyphVolume = "", GlyphMute = "", GlyphWarning = "", GlyphApp = "", GlyphSpeakers = "";

    readonly DispatcherTimer hideTimer = new();
    FeedbackState? pending;
    int renderScheduled;
    OverlayConfig config = new();

    public OverlayWindow()
    {
        InitializeComponent();
        hideTimer.Tick += (_, _) => { hideTimer.Stop(); FadeOut(); };
        ApplyTheme();
    }

    public void Configure(OverlayConfig cfg)
    {
        config = cfg;
        ApplyTheme();
        if (IsVisible) PositionWindow();
    }

    /// <summary>Thread-safe. Coalesce: chamadas em sequência rápida desenham só o último estado.</summary>
    public void Push(FeedbackState state)
    {
        Volatile.Write(ref pending, state);
        if (Interlocked.Exchange(ref renderScheduled, 1) == 0)
            Dispatcher.BeginInvoke(DispatcherPriority.Render, Render);
    }

    void Render()
    {
        Interlocked.Exchange(ref renderScheduled, 0);
        var s = Volatile.Read(ref pending);
        if (s is null || !config.Enabled) return;

        LabelText.Text = s.Label;
        bool hasBar = s.Percent is not null && s.Kind is FeedbackKind.DeviceVolume or FeedbackKind.FocusVolume;
        Glyph.Text = s.Kind switch
        {
            FeedbackKind.Unavailable or FeedbackKind.Error => GlyphWarning,
            FeedbackKind.DeviceSwitch => GlyphSpeakers,
            FeedbackKind.FocusVolume => s.Muted ? GlyphMute : GlyphApp,
            _ => s.Muted ? GlyphMute : GlyphVolume,
        };
        ValueText.Text = hasBar ? (s.Muted ? "Mudo" : $"{s.Percent}%") : "";
        BarTrackHost.Visibility = hasBar ? Visibility.Visible : Visibility.Collapsed;
        DetailText.Visibility = hasBar ? Visibility.Collapsed : Visibility.Visible;
        DetailText.Text = s.Detail ?? "";
        if (hasBar)
        {
            BarTrackHost.UpdateLayout();
            double w = BarTrackHost.ActualWidth > 0 ? BarTrackHost.ActualWidth : Width - 36;
            BarFill.Width = Math.Round(w * s.Percent!.Value / 100.0);
        }

        if (!IsVisible) { Opacity = 1; Show(); PositionWindow(); }
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        hideTimer.Stop();
        hideTimer.Interval = TimeSpan.FromMilliseconds(s.Kind == FeedbackKind.DeviceSwitch ? config.DeviceSwitchHideAfterMs : config.HideAfterMs);
        hideTimer.Start();
    }

    void FadeOut()
    {
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        anim.Completed += (_, _) => { if (Opacity == 0) Hide(); };
        BeginAnimation(OpacityProperty, anim);
    }

    void PositionWindow()
    {
        var area = SystemParameters.WorkArea; // monitor principal, em DIPs
        double m = config.Margin;
        var (h, v) = config.Position.ToLowerInvariant() switch
        {
            "top-center" => ("center", "top"),
            "top-right" => ("right", "top"),
            "bottom-left" => ("left", "bottom"),
            "bottom-center" => ("center", "bottom"),
            "bottom-right" => ("right", "bottom"),
            _ => ("left", "top"),
        };
        Left = h switch { "center" => area.Left + (area.Width - Width) / 2, "right" => area.Right - Width - m, _ => area.Left + m };
        Top = v == "bottom" ? area.Bottom - Height - m : area.Top + m;
    }

    void ApplyTheme()
    {
        bool light = string.Equals(config.Theme, "light", StringComparison.OrdinalIgnoreCase);
        Card.Background = Brush(light ? "#F7FFFFFF" : "#EB1F1F1F");
        Card.BorderBrush = Brush(light ? "#22000000" : "#33FFFFFF");
        Card.BorderThickness = new Thickness(1);
        var fg = Brush(light ? "#FF1A1A1A" : "#FFF2F2F2");
        Glyph.Foreground = LabelText.Foreground = ValueText.Foreground = fg;
        DetailText.Foreground = Brush(light ? "#FF5A5A5A" : "#FFB8B8B8");
        BarTrack.Background = Brush(light ? "#1F000000" : "#33FFFFFF");
        BarFill.Background = Brush("#FF4C9AFF");
    }

    static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    // ---- nunca pegar foco / click-through (RNF crítico do doc 05) ----

    const int GWL_EXSTYLE = -20;
    const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TOPMOST;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>Handle da janela, para o rastreador de foco ignorá-la (Fase 2).</summary>
    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();
}
