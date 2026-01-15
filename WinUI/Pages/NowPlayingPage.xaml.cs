using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using AppleMusicRpc.Services;

namespace AppleMusicRpc.Pages;

public sealed partial class NowPlayingPage : Page
{
    private readonly RpcService _rpc;
    private readonly DispatcherTimer _progressTimer;
    private bool _hasTrack;
    private double _trackDuration;
    private double _trackPosition;
    private DateTime _lastUpdateTime;

    public NowPlayingPage()
    {
        InitializeComponent();
        _rpc = RpcService.Instance;
        _rpc.TrackChanged += OnTrackChanged;

        // Timer to update progress bar
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _progressTimer.Tick += OnProgressTimerTick;

        // Load current state with synced position
        if (_rpc.CurrentTrack != null)
        {
            LoadCurrentTrackWithSync();
        }
        UpdateLastFmStatus();
        UpdateButtonStates();
    }

    private void LoadCurrentTrackWithSync()
    {
        var track = _rpc.CurrentTrack;
        if (track == null) return;

        _hasTrack = true;
        TrackTitle.Text = track.Title;
        TrackArtist.Text = track.Artist;
        TrackAlbum.Text = track.Album;

        // Load album art
        if (!string.IsNullOrEmpty(track.AlbumArtUrl))
        {
            try
            {
                AlbumArtImage.Source = new BitmapImage(new Uri(track.AlbumArtUrl));
                PlaceholderIcon.Visibility = Visibility.Collapsed;
            }
            catch
            {
                PlaceholderIcon.Visibility = Visibility.Visible;
            }
        }
        else
        {
            AlbumArtImage.Source = null;
            PlaceholderIcon.Visibility = Visibility.Visible;
        }

        OpenButton.Visibility = !string.IsNullOrEmpty(track.AppleMusicUrl)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Calculate estimated current position based on when RpcService last updated
        var elapsed = (DateTime.Now - _rpc.LastTrackUpdateTime).TotalSeconds;
        var estimatedPosition = Math.Min(track.Position + elapsed, track.Duration);

        _trackDuration = track.Duration;
        _trackPosition = estimatedPosition;
        _lastUpdateTime = DateTime.Now;

        if (_trackDuration > 0)
        {
            ProgressSection.Visibility = Visibility.Visible;
            UpdateProgress();
            _progressTimer.Start();
        }
        else
        {
            ProgressSection.Visibility = Visibility.Collapsed;
            _progressTimer.Stop();
        }

        UpdateButtonStates();
    }

    private void OnTrackChanged(TrackInfo? track)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _hasTrack = track != null;

            if (track == null)
            {
                TrackTitle.Text = "Not playing";
                TrackArtist.Text = "Play something in Apple Music";
                TrackAlbum.Text = "";
                AlbumArtImage.Source = null;
                PlaceholderIcon.Visibility = Visibility.Visible;
                ProgressSection.Visibility = Visibility.Collapsed;
                _progressTimer.Stop();
            }
            else
            {
                TrackTitle.Text = track.Title;
                TrackArtist.Text = track.Artist;
                TrackAlbum.Text = track.Album;

                // Load album art
                if (!string.IsNullOrEmpty(track.AlbumArtUrl))
                {
                    try
                    {
                        AlbumArtImage.Source = new BitmapImage(new Uri(track.AlbumArtUrl));
                        PlaceholderIcon.Visibility = Visibility.Collapsed;
                    }
                    catch
                    {
                        PlaceholderIcon.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    AlbumArtImage.Source = null;
                    PlaceholderIcon.Visibility = Visibility.Visible;
                }

                // Show Open button only if we have an Apple Music URL
                OpenButton.Visibility = !string.IsNullOrEmpty(track.AppleMusicUrl)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                // Update progress - track changed so position is fresh
                _trackDuration = track.Duration;
                _trackPosition = track.Position;
                _lastUpdateTime = DateTime.Now;

                if (_trackDuration > 0)
                {
                    ProgressSection.Visibility = Visibility.Visible;
                    UpdateProgress();
                    _progressTimer.Start();
                }
                else
                {
                    ProgressSection.Visibility = Visibility.Collapsed;
                    _progressTimer.Stop();
                }
            }

            UpdateButtonStates();
        });
    }

    private void OnProgressTimerTick(object? sender, object e)
    {
        if (_trackDuration <= 0) return;

        // Estimate current position based on elapsed time
        var elapsed = (DateTime.Now - _lastUpdateTime).TotalSeconds;
        var estimatedPosition = _trackPosition + elapsed;

        if (estimatedPosition > _trackDuration)
            estimatedPosition = _trackDuration;

        UpdateProgressDisplay(estimatedPosition);
    }

    private void UpdateProgress()
    {
        UpdateProgressDisplay(_trackPosition);
    }

    private void UpdateProgressDisplay(double position)
    {
        if (_trackDuration <= 0) return;

        var percentage = (position / _trackDuration) * 100;
        TrackProgress.Value = Math.Min(percentage, 100);
        CurrentTime.Text = FormatTime(position);

        var remaining = _trackDuration - position;
        RemainingTime.Text = $"-{FormatTime(Math.Max(0, remaining))}";
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    private void UpdateButtonStates()
    {
        // Show action buttons when we have a track or RPC is active
        ActionButtons.Visibility = (_hasTrack || _rpc.IsPaused)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Update pause button state
        if (_rpc.IsPaused)
        {
            PauseIcon.Glyph = "\uE768";
            PauseText.Text = "Resume RPC";
        }
        else
        {
            PauseIcon.Glyph = "\uE769";
            PauseText.Text = "Pause RPC";
        }
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_rpc.IsPaused)
            {
                _rpc.Resume();
            }
            else
            {
                _rpc.Pause();
            }
            UpdateButtonStates();
        }
        catch { }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var url = _rpc.CurrentTrack?.AppleMusicUrl;
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                Windows.System.Launcher.LaunchUriAsync(new Uri(url));
            }
            catch { }
        }
    }

    private void UpdateLastFmStatus()
    {
        var config = ConfigService.Load();
        var hasLastFm = !string.IsNullOrEmpty(config.LastFmApiKey);

        // Only show Last.fm section if configured
        LastFmSection.Visibility = hasLastFm ? Visibility.Visible : Visibility.Collapsed;

        if (!hasLastFm)
        {
            LastFmStatus.Text = "Not configured";
        }
        else if (!string.IsNullOrEmpty(config.LastFmSessionKey))
        {
            LastFmStatus.Text = config.LastFmScrobblingEnabled ? "Connected (scrobbling)" : "Connected";
        }
        else
        {
            LastFmStatus.Text = "Not connected";
        }
    }

    private void LastFmHelpButton_Click(object sender, RoutedEventArgs e)
    {
        LastFmHelpPanel.Visibility = LastFmHelpPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void GoToSettings_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to settings page
        if (Frame != null)
        {
            Frame.Navigate(typeof(SettingsPage));
        }
    }
}
