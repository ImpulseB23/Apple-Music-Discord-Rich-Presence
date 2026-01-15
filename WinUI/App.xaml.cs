using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AppleMusicRpc.Services;

namespace AppleMusicRpc;

public partial class App : Microsoft.UI.Xaml.Application
{
    private MainWindow? _window;
    private NotifyIcon? _trayIcon;
    private ContextMenuStrip? _contextMenu;
    private ToolStripMenuItem? _nowPlayingItem;
    private ToolStripMenuItem? _pauseResumeItem;
    private ToolStripMenuItem? _openInAppleMusicItem;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
        SetupTrayIcon();

        // Subscribe to track changes for tray updates
        RpcService.Instance.TrackChanged += OnTrackChanged;
        RpcService.Instance.StatusChanged += OnStatusChanged;
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon();

        // Load icon
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icon.ico");
            if (File.Exists(iconPath))
                _trayIcon.Icon = new Icon(iconPath);
            else
                _trayIcon.Icon = SystemIcons.Application;
        }
        catch
        {
            _trayIcon.Icon = SystemIcons.Application;
        }

        _trayIcon.Text = "Apple Music Discord RPC";
        _trayIcon.Visible = true;

        // Build context menu
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Renderer = new ModernMenuRenderer();

        // Now Playing header (disabled, shows current track)
        _nowPlayingItem = new ToolStripMenuItem("Not Playing");
        _nowPlayingItem.Enabled = false;
        _nowPlayingItem.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _contextMenu.Items.Add(_nowPlayingItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Pause/Resume RPC
        _pauseResumeItem = new ToolStripMenuItem("Pause RPC");
        _pauseResumeItem.Click += (s, e) =>
        {
            if (RpcService.Instance.IsPaused)
                RpcService.Instance.Resume();
            else
                RpcService.Instance.Pause();
            UpdatePauseResumeText();
        };
        _contextMenu.Items.Add(_pauseResumeItem);

        // Open in Apple Music (hidden when no track)
        _openInAppleMusicItem = new ToolStripMenuItem("Play on Apple Music");
        _openInAppleMusicItem.Click += (s, e) =>
        {
            var url = RpcService.Instance.CurrentTrack?.AppleMusicUrl;
            if (!string.IsNullOrEmpty(url))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        };
        _openInAppleMusicItem.Visible = false;
        _contextMenu.Items.Add(_openInAppleMusicItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Show Window
        var showItem = new ToolStripMenuItem("Open App");
        showItem.Click += (s, e) => _window?.ShowWindow();
        _contextMenu.Items.Add(showItem);

        // Mini Mode toggle
        var miniModeItem = new ToolStripMenuItem("Mini Mode");
        miniModeItem.Click += (s, e) =>
        {
            _window?.ShowWindow();
            // Toggle mini mode via the public method
            typeof(MainWindow).GetMethod("ToggleMiniMode",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(_window, null);
        };
        _contextMenu.Items.Add(miniModeItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Exit
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => ExitApp();
        _contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = _contextMenu;

        // Double-click to show
        _trayIcon.DoubleClick += (s, e) => _window?.ShowWindow();
    }

    private void OnTrackChanged(TrackInfo? track)
    {
        if (_nowPlayingItem == null || _openInAppleMusicItem == null) return;

        if (track != null)
        {
            // Update tooltip with track info
            var tooltip = $"{track.Title}\n{track.Artist}";
            if (tooltip.Length > 63) tooltip = tooltip[..60] + "...";
            if (_trayIcon != null) _trayIcon.Text = tooltip;

            // Update now playing menu item
            var displayText = $"{track.Artist} — {track.Title}";
            if (displayText.Length > 40) displayText = displayText[..37] + "...";
            _nowPlayingItem.Text = displayText;

            // Show Open in Apple Music if URL is available
            _openInAppleMusicItem.Visible = !string.IsNullOrEmpty(track.AppleMusicUrl);
        }
        else
        {
            if (_trayIcon != null) _trayIcon.Text = "Apple Music Discord RPC";
            _nowPlayingItem.Text = "Not Playing";
            _openInAppleMusicItem.Visible = false;
        }
    }

    private void OnStatusChanged(string status, bool isError)
    {
        UpdatePauseResumeText();
    }

    private void UpdatePauseResumeText()
    {
        if (_pauseResumeItem == null) return;
        _pauseResumeItem.Text = RpcService.Instance.IsPaused ? "Resume RPC" : "Pause RPC";
    }

    private void ExitApp()
    {
        RpcService.Instance.TrackChanged -= OnTrackChanged;
        RpcService.Instance.StatusChanged -= OnStatusChanged;
        _trayIcon?.Dispose();
        RpcService.Instance.Dispose();
        _window?.ReallyClose();
        Environment.Exit(0);
    }

    public static MainWindow? MainWindow => (Current as App)?._window;
}

// Custom renderer for a more modern look
internal class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    public ModernMenuRenderer() : base(new ModernColorTable()) { }
}

internal class ModernColorTable : ProfessionalColorTable
{
    public override Color MenuItemSelected => Color.FromArgb(50, 50, 50);
    public override Color MenuItemBorder => Color.FromArgb(60, 60, 60);
    public override Color MenuBorder => Color.FromArgb(45, 45, 45);
    public override Color ToolStripDropDownBackground => Color.FromArgb(32, 32, 32);
    public override Color ImageMarginGradientBegin => Color.FromArgb(32, 32, 32);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(32, 32, 32);
    public override Color ImageMarginGradientEnd => Color.FromArgb(32, 32, 32);
    public override Color SeparatorDark => Color.FromArgb(60, 60, 60);
    public override Color SeparatorLight => Color.FromArgb(60, 60, 60);
}
