using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using AppleMusicRpc.Services;

namespace AppleMusicRpc.Pages;

public sealed partial class StatisticsPage : Page
{
    private readonly StatisticsService _stats;
    private readonly DispatcherTimer _uptimeTimer;

    public StatisticsPage()
    {
        InitializeComponent();
        _stats = StatisticsService.Instance;

        // Subscribe to stats updates
        _stats.StatsUpdated += OnStatsUpdated;

        // Timer to update uptime
        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += OnUptimeTick;
        _uptimeTimer.Start();

        // Initialize display
        UpdateStats();
    }

    private void OnStatsUpdated()
    {
        DispatcherQueue.TryEnqueue(UpdateStats);
    }

    private void UpdateStats()
    {
        TracksPlayed.Text = _stats.TrackCount.ToString();
        TimeListened.Text = StatisticsService.FormatDuration(_stats.TotalListenTime);
        Scrobbles.Text = _stats.ScrobbleCount.ToString();
        UpdateTopArtists();
    }

    private void UpdateTopArtists()
    {
        TopArtistsList.Children.Clear();

        var topArtists = _stats.GetTopArtists(5);
        var hasArtists = false;

        foreach (var (artist, count) in topArtists)
        {
            hasArtists = true;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var artistText = new TextBlock
            {
                Text = artist,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var countText = new TextBlock
            {
                Text = $"{count} plays",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            Grid.SetColumn(countText, 1);

            row.Children.Add(artistText);
            row.Children.Add(countText);
            TopArtistsList.Children.Add(row);
        }

        if (!hasArtists)
        {
            TopArtistsList.Children.Add(new TextBlock
            {
                Text = "No artists yet. Play some music!",
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            });
        }
    }

    private void OnUptimeTick(object? sender, object e)
    {
        var uptime = DateTime.Now - _stats.SessionStart;
        Uptime.Text = uptime.ToString(@"h\:mm\:ss");
    }
}
