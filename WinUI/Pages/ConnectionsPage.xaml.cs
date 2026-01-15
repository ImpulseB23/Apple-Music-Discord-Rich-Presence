using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using AppleMusicRpc.Services;

namespace AppleMusicRpc.Pages;

public sealed partial class ConnectionsPage : Page
{
    private static readonly SolidColorBrush GreenBrush = new(ColorHelper.FromArgb(255, 108, 203, 95));
    private static readonly SolidColorBrush RedBrush = new(ColorHelper.FromArgb(255, 232, 17, 35));
    private static readonly SolidColorBrush YellowBrush = new(ColorHelper.FromArgb(255, 252, 185, 0));
    private static readonly SolidColorBrush GrayBrush = new(ColorHelper.FromArgb(255, 140, 140, 140));

    public ConnectionsPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateConnectionStatus();
    }

    private void UpdateConnectionStatus()
    {
        var rpc = RpcService.Instance;

        // Discord status
        if (rpc.CurrentTrack != null)
        {
            DiscordStatus.Text = "Connected and active";
            DiscordDot.Fill = GreenBrush;
        }
        else if (rpc.IsPaused)
        {
            DiscordStatus.Text = "Paused";
            DiscordDot.Fill = YellowBrush;
        }
        else
        {
            DiscordStatus.Text = "Connected, waiting for music";
            DiscordDot.Fill = GreenBrush;
        }

        // Apple Music status
        if (rpc.CurrentTrack != null)
        {
            AppleMusicStatus.Text = $"Playing: {rpc.CurrentTrack.Title}";
            AppleMusicDot.Fill = GreenBrush;
        }
        else
        {
            AppleMusicStatus.Text = "No music detected";
            AppleMusicDot.Fill = GrayBrush;
        }

        // Last.fm status
        var config = ConfigService.Load();
        if (string.IsNullOrEmpty(config.LastFmApiKey))
        {
            LastFmStatus.Text = "Not configured";
            LastFmDot.Fill = GrayBrush;
            ConfigureLastFmButton.Visibility = Visibility.Visible;
        }
        else if (string.IsNullOrEmpty(config.LastFmSessionKey))
        {
            LastFmStatus.Text = "Not authenticated";
            LastFmDot.Fill = YellowBrush;
            ConfigureLastFmButton.Visibility = Visibility.Visible;
        }
        else if (config.LastFmScrobblingEnabled)
        {
            LastFmStatus.Text = "Connected (scrobbling enabled)";
            LastFmDot.Fill = GreenBrush;
            ConfigureLastFmButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            LastFmStatus.Text = "Connected (scrobbling disabled)";
            LastFmDot.Fill = YellowBrush;
            ConfigureLastFmButton.Visibility = Visibility.Collapsed;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        UpdateConnectionStatus();
    }

    private void ConfigureLastFm_Click(object sender, RoutedEventArgs e)
    {
        Frame?.Navigate(typeof(SettingsPage));
    }
}
