using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Windows.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using AppleMusicRpc.Services;

namespace AppleMusicRpc;

public partial class App : Application
{
    private MainWindow? _window;
    private TaskbarIcon? _trayIcon;
    private MenuFlyout? _contextMenu;
    private MenuFlyoutItem? _nowPlayingItem;
    private MenuFlyoutItem? _pauseResumeItem;
    private MenuFlyoutItem? _showHideItem;
    private MenuFlyoutItem? _miniModeItem;
    private bool _isWindowVisible = true;
    private bool _isInMiniMode = false;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();

        var config = ConfigService.Load();

        // Start minimized to tray if configured
        if (config.StartMinimizedToTray)
        {
            // Don't activate the window, just set it up
            _isWindowVisible = false;
        }
        else
        {
            _window.Activate();
        }

        SetupTrayIcon();

        // Subscribe to track changes for tray updates
        RpcService.Instance.TrackChanged += OnTrackChanged;
        RpcService.Instance.StatusChanged += OnStatusChanged;

        // Check for updates if enabled
        if (config.CheckForUpdatesAutomatically)
        {
            await UpdateService.Instance.CheckForUpdatesAsync();
        }
    }

    private void SetupTrayIcon()
    {
        // Build context menu with WinUI MenuFlyout (modern Windows 11 style)
        _contextMenu = new MenuFlyout();

        // Now Playing header (disabled, shows current track)
        _nowPlayingItem = new MenuFlyoutItem
        {
            Text = "Not Playing",
            IsEnabled = false,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        };
        _contextMenu.Items.Add(_nowPlayingItem);

        _contextMenu.Items.Add(new MenuFlyoutSeparator());

        // Pause/Resume RPC
        _pauseResumeItem = new MenuFlyoutItem
        {
            Text = "Pause RPC",
            Icon = new FontIcon { Glyph = "\uE769" }
        };
        _pauseResumeItem.Click += (s, e) =>
        {
            if (RpcService.Instance.IsPaused)
                RpcService.Instance.Resume();
            else
                RpcService.Instance.Pause();
            UpdatePauseResumeText();
        };
        _contextMenu.Items.Add(_pauseResumeItem);

        _contextMenu.Items.Add(new MenuFlyoutSeparator());

        // Show/Hide Window - changes based on window visibility
        _showHideItem = new MenuFlyoutItem
        {
            Text = "Minimize",
            Icon = new FontIcon { Glyph = "\uE921" },
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        _showHideItem.Click += (s, e) => ToggleWindowVisibility();
        _contextMenu.Items.Add(_showHideItem);

        // Mini Mode toggle
        _miniModeItem = new MenuFlyoutItem
        {
            Text = "Mini Mode",
            Icon = new FontIcon { Glyph = "\uE73F" }
        };
        _miniModeItem.Click += (s, e) =>
        {
            ShowWindowInternal();
            ToggleMiniMode();
        };
        _contextMenu.Items.Add(_miniModeItem);

        _contextMenu.Items.Add(new MenuFlyoutSeparator());

        // Exit
        var exitItem = new MenuFlyoutItem
        {
            Text = "Exit",
            Icon = new FontIcon { Glyph = "\uE7E8" }
        };
        exitItem.Click += (s, e) => ExitApp();
        _contextMenu.Items.Add(exitItem);

        // Create TaskbarIcon
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Apple Music Discord RPC",
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = _contextMenu
        };

        // Set icon from file
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icon.ico");
        if (File.Exists(iconPath))
        {
            _trayIcon.Icon = new Icon(iconPath);
        }

        // Double-click to toggle visibility
        _trayIcon.DoubleClickCommand = new RelayCommand(ToggleWindowVisibility);

        // Ensure the tray icon is created
        _trayIcon.ForceCreate();
    }

    private void ToggleMiniMode()
    {
        // Toggle mini mode via the public method
        typeof(MainWindow).GetMethod("ToggleMiniMode",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(_window, null);

        _isInMiniMode = !_isInMiniMode;
        UpdateMiniModeText();
    }

    private void UpdateMiniModeText()
    {
        if (_miniModeItem == null) return;
        _miniModeItem.Text = _isInMiniMode ? "Normal Mode" : "Mini Mode";
        _miniModeItem.Icon = new FontIcon { Glyph = _isInMiniMode ? "\uE740" : "\uE73F" };
    }

    private void ToggleWindowVisibility()
    {
        if (_isWindowVisible)
        {
            HideWindowInternal();
        }
        else
        {
            ShowWindowInternal();
        }
    }

    private void ShowWindowInternal()
    {
        _window?.ShowWindow();
        _isWindowVisible = true;
        UpdateShowHideText();
    }

    private void HideWindowInternal()
    {
        _window?.HideWindow();
        _isWindowVisible = false;
        UpdateShowHideText();
    }

    private void UpdateShowHideText()
    {
        if (_showHideItem == null) return;
        _showHideItem.Text = _isWindowVisible ? "Minimize" : "Open";
        _showHideItem.Icon = new FontIcon { Glyph = _isWindowVisible ? "\uE921" : "\uE8A7" };
    }

    public void NotifyWindowHidden()
    {
        _isWindowVisible = false;
        UpdateShowHideText();
    }

    public void NotifyWindowShown()
    {
        _isWindowVisible = true;
        UpdateShowHideText();
    }

    public void NotifyMiniModeChanged(bool isInMiniMode)
    {
        _isInMiniMode = isInMiniMode;
        _window?.DispatcherQueue.TryEnqueue(UpdateMiniModeText);
    }

    private void OnTrackChanged(TrackInfo? track)
    {
        if (_nowPlayingItem == null || _trayIcon == null) return;

        // Need to dispatch to UI thread
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            if (track != null)
            {
                // Update tooltip with track info
                var tooltip = $"{track.Title}\n{track.Artist}";
                if (tooltip.Length > 63) tooltip = tooltip[..60] + "...";
                _trayIcon.ToolTipText = tooltip;

                // Update now playing menu item
                var displayText = $"{track.Artist} — {track.Title}";
                if (displayText.Length > 40) displayText = displayText[..37] + "...";
                _nowPlayingItem.Text = displayText;
            }
            else
            {
                _trayIcon.ToolTipText = "Apple Music Discord RPC";
                _nowPlayingItem.Text = "Not Playing";
            }
        });
    }

    private void OnStatusChanged(string status, bool isError)
    {
        _window?.DispatcherQueue.TryEnqueue(UpdatePauseResumeText);
    }

    private void UpdatePauseResumeText()
    {
        if (_pauseResumeItem == null) return;
        _pauseResumeItem.Text = RpcService.Instance.IsPaused ? "Resume RPC" : "Pause RPC";
        _pauseResumeItem.Icon = new FontIcon { Glyph = RpcService.Instance.IsPaused ? "\uE768" : "\uE769" };
    }

    internal void ExitApp()
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

// Simple RelayCommand implementation
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    public RelayCommand(Action execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
}
