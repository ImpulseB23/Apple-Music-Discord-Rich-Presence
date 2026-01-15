using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;
using AppleMusicRpc.Services;

namespace AppleMusicRpc;

public sealed partial class MainWindow : Window
{
    private AppWindow? _appWindow;
    private bool _isMiniMode;
    private SizeInt32 _normalSize = new(850, 550);
    private PointInt32 _normalPosition;
    private static readonly SizeInt32 MiniSize = new(300, 80);
    private bool _isReallyClosing;
    private IntPtr _hwnd;

    // Mini mode progress timer
    private readonly DispatcherTimer _miniProgressTimer;
    private double _miniTrackPosition;
    private double _miniTrackDuration;
    private DateTime _miniLastUpdateTime;

    // Win32 APIs for always on top
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    // Window subclass for hotkey messages
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProc newProc);
    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private const int GWLP_WNDPROC = -4;
    private IntPtr _originalWndProc;
    private WndProc? _newWndProc;

    public MainWindow()
    {
        InitializeComponent();
        TrySetMicaBackdrop();
        ConfigureWindow();
        LoadTitleBarIcon();
        ContentFrame.Navigate(typeof(Pages.NowPlayingPage));

        // Subscribe to track changes for mini mode updates
        RpcService.Instance.TrackChanged += OnTrackChanged;

        // Initialize mini mode progress timer
        _miniProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _miniProgressTimer.Tick += OnMiniProgressTick;

        // Initialize global hotkeys
        InitializeHotkeys();

        // Apply startup settings
        ApplyStartupSettings();
    }

    private void ApplyStartupSettings()
    {
        var config = ConfigService.Load();

        // Apply always on top
        if (config.AlwaysOnTop)
        {
            SetAlwaysOnTop(true);
        }

        // Apply expanded sidebar
        if (config.StartWithExpandedSidebar)
        {
            NavView.IsPaneOpen = true;
        }

        // Apply start in mini mode (delay slightly to ensure window is ready)
        if (config.StartInMiniMode)
        {
            DispatcherQueue.TryEnqueue(() => ToggleMiniMode());
        }
    }

    private void InitializeHotkeys()
    {
        // Subclass the window to receive WM_HOTKEY messages
        _newWndProc = new WndProc(HotkeyWndProc);
        _originalWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _newWndProc);

        // Initialize the hotkey service
        HotkeyService.Instance.Initialize(_hwnd);
        HotkeyService.Instance.HotkeyPressed += OnHotkeyPressed;
    }

    private IntPtr HotkeyWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == HotkeyService.WM_HOTKEY)
        {
            HotkeyService.Instance.ProcessHotkey((int)wParam);
        }
        return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    private void OnHotkeyPressed(HotkeyAction action)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (action)
            {
                case HotkeyAction.ToggleMiniMode:
                    ToggleMiniMode();
                    break;
                case HotkeyAction.PauseResumeRpc:
                    if (RpcService.Instance.IsPaused)
                        RpcService.Instance.Resume();
                    else
                        RpcService.Instance.Pause();
                    break;
            }
        });
    }

    public void SetAlwaysOnTop(bool alwaysOnTop)
    {
        if (_isMiniMode) return; // Mini mode handles its own always-on-top

        if (alwaysOnTop)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        }
        else
        {
            SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        }
    }

    private void ConfigureWindow()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow != null)
        {
            _appWindow.Resize(new SizeInt32(850, 550));

            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = _appWindow.TitleBar;
                titleBar.ExtendsContentIntoTitleBar = true;
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

                // Set drag region to exclude the left 48px (where menu button is)
                titleBar.SetDragRectangles(new RectInt32[]
                {
                    new RectInt32(48, 0, 1000, 32)
                });
            }

            _appWindow.Title = "Apple Music RPC";

            // Set taskbar icon
            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icon.ico");
                if (File.Exists(iconPath))
                    _appWindow.SetIcon(iconPath);
            }
            catch { }
        }
    }

    private void LoadTitleBarIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icon.png");
            if (File.Exists(iconPath))
            {
                TitleBarIcon.Source = new BitmapImage(new Uri(iconPath));
            }
        }
        catch { }
    }

    private bool TrySetMicaBackdrop()
    {
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
            return true;
        }
        return false;
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        // Prevent closing, minimize to taskbar instead
        if (!_isReallyClosing)
        {
            args.Handled = true;
            ShowWindow(_hwnd, SW_HIDE);
        }
    }

    public void ShowWindow()
    {
        ShowWindow(_hwnd, SW_SHOW);
        _appWindow?.Show();
    }

    public void ReallyClose()
    {
        _isReallyClosing = true;
        RpcService.Instance.TrackChanged -= OnTrackChanged;
        HotkeyService.Instance.HotkeyPressed -= OnHotkeyPressed;
        HotkeyService.Instance.Dispose();
        Close();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();

            // Handle Mini Mode toggle specially - it's a button, not a page
            if (tag == "MiniMode")
            {
                ToggleMiniMode();
                // Reset selection back to Now Playing since Mini Mode is just a toggle button
                SelectNowPlayingNavItem();
                return;
            }

            Type? pageType = tag switch
            {
                "NowPlaying" => typeof(Pages.NowPlayingPage),
                "History" => typeof(Pages.HistoryPage),
                "Statistics" => typeof(Pages.StatisticsPage),
                "Connections" => typeof(Pages.ConnectionsPage),
                "Appearance" => typeof(Pages.AppearancePage),
                "Shortcuts" => typeof(Pages.ShortcutsPage),
                "Settings" => typeof(Pages.SettingsPage),
                "About" => typeof(Pages.AboutPage),
                _ => null
            };

            if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }

    private void SelectNowPlayingNavItem()
    {
        // Find and select the Now Playing nav item
        foreach (var menuItem in NavView.MenuItems)
        {
            if (menuItem is NavigationViewItem navItem && navItem.Tag?.ToString() == "NowPlaying")
            {
                NavView.SelectedItem = navItem;
                break;
            }
        }
    }

    private void ToggleMiniMode()
    {
        if (_appWindow == null) return;

        _isMiniMode = !_isMiniMode;

        if (_isMiniMode)
        {
            // Save current size and position
            _normalSize = new SizeInt32(_appWindow.Size.Width, _appWindow.Size.Height);
            _normalPosition = _appWindow.Position;

            // Hide normal content, show mini content
            NormalModeContent.Visibility = Visibility.Collapsed;
            MiniModeContent.Visibility = Visibility.Visible;

            // Update mini mode with current track (sync with current playback position)
            SyncMiniModeWithCurrentPlayback();

            // Start progress timer
            _miniProgressTimer.Start();

            // Resize and position at bottom-right
            _appWindow.Resize(MiniSize);
            PositionBottomRight();

            // Set always on top
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);

            // Configure title bar for mini mode (minimal)
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = _appWindow.TitleBar;
                titleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
            }
        }
        else
        {
            // Stop progress timer
            _miniProgressTimer.Stop();

            // Restore always on top based on config setting
            var config = ConfigService.Load();
            if (config.AlwaysOnTop)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            }
            else
            {
                SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            }

            // Show normal content, hide mini content
            NormalModeContent.Visibility = Visibility.Visible;
            MiniModeContent.Visibility = Visibility.Collapsed;

            // Restore title bar
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = _appWindow.TitleBar;
                titleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
                titleBar.SetDragRectangles(new RectInt32[]
                {
                    new RectInt32(48, 0, 1000, 32)
                });
            }

            // Restore size and position
            _appWindow.Resize(_normalSize);
            _appWindow.Move(_normalPosition);

            // Navigate back to Now Playing and select it in nav
            ContentFrame.Navigate(typeof(Pages.NowPlayingPage));
            SelectNowPlayingNavItem();
        }
    }

    private void PositionBottomRight()
    {
        if (_appWindow == null) return;

        var displayArea = DisplayArea.GetFromWindowId(
            Win32Interop.GetWindowIdFromWindow(_hwnd),
            DisplayAreaFallback.Primary);

        if (displayArea != null)
        {
            var workArea = displayArea.WorkArea;
            var x = workArea.X + workArea.Width - MiniSize.Width - 16;
            var y = workArea.Y + workArea.Height - MiniSize.Height - 16;
            _appWindow.Move(new PointInt32(x, y));
        }
    }

    private void OnTrackChanged(TrackInfo? track)
    {
        if (_isMiniMode)
        {
            DispatcherQueue.TryEnqueue(() => UpdateMiniModeContent());
        }
    }

    private void SyncMiniModeWithCurrentPlayback()
    {
        var track = RpcService.Instance.CurrentTrack;
        if (track != null)
        {
            MiniTrackTitle.Text = track.Title;
            MiniTrackArtist.Text = track.Artist;

            // Calculate estimated current position based on when RpcService last updated
            var lastUpdate = RpcService.Instance.LastTrackUpdateTime;
            var elapsed = (DateTime.Now - lastUpdate).TotalSeconds;
            var estimatedPosition = Math.Min(track.Position + elapsed, track.Duration);

            // Update progress tracking for timer
            _miniTrackDuration = track.Duration;
            _miniTrackPosition = estimatedPosition;
            _miniLastUpdateTime = DateTime.Now;

            // Update progress bar with synced position
            if (track.Duration > 0)
            {
                MiniProgressBar.Value = (estimatedPosition / track.Duration) * 100;
            }

            // Update album art
            if (!string.IsNullOrEmpty(track.AlbumArtUrl))
            {
                try
                {
                    MiniAlbumArt.Source = new BitmapImage(new Uri(track.AlbumArtUrl));
                    MiniPlaceholderIcon.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    MiniPlaceholderIcon.Visibility = Visibility.Visible;
                }
            }
            else
            {
                MiniAlbumArt.Source = null;
                MiniPlaceholderIcon.Visibility = Visibility.Visible;
            }
        }
        else
        {
            MiniTrackTitle.Text = "Not playing";
            MiniTrackArtist.Text = "";
            MiniProgressBar.Value = 0;
            MiniAlbumArt.Source = null;
            MiniPlaceholderIcon.Visibility = Visibility.Visible;
            _miniTrackDuration = 0;
            _miniTrackPosition = 0;
        }
    }

    private void UpdateMiniModeContent()
    {
        // Called when track changes - sync with fresh track data
        SyncMiniModeWithCurrentPlayback();
    }

    private void OnMiniProgressTick(object? sender, object e)
    {
        if (_miniTrackDuration <= 0) return;

        // Estimate current position based on elapsed time
        var elapsed = (DateTime.Now - _miniLastUpdateTime).TotalSeconds;
        var position = Math.Min(_miniTrackPosition + elapsed, _miniTrackDuration);
        MiniProgressBar.Value = (position / _miniTrackDuration) * 100;
    }

    private void MiniMode_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // Fade in the expand button
        var fadeIn = new DoubleAnimation
        {
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(150))
        };
        Storyboard.SetTarget(fadeIn, ExpandButton);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(fadeIn);
        storyboard.Begin();
    }

    private void MiniMode_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // Fade out the expand button
        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(150))
        };
        Storyboard.SetTarget(fadeOut, ExpandButton);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(fadeOut);
        storyboard.Begin();
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isMiniMode)
        {
            ToggleMiniMode();
        }
    }

    public void ExitMiniMode()
    {
        if (_isMiniMode)
        {
            ToggleMiniMode();
        }
    }
}
