using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.IO;
using AppleMusicRpc.Services;

namespace AppleMusicRpc.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icon.png");
            if (File.Exists(iconPath))
            {
                AppIcon.Source = new BitmapImage(new Uri(iconPath));
            }
        }
        catch { }

        // Set current version
        VersionText.Text = $"Version {UpdateService.Instance.CurrentVersion}";

        // Check if update is available
        UpdateUI();

        // Subscribe to update events
        UpdateService.Instance.UpdateFound += OnUpdateFound;
    }

    private void OnUpdateFound(string current, string latest)
    {
        DispatcherQueue.TryEnqueue(UpdateUI);
    }

    private void UpdateUI()
    {
        if (UpdateService.Instance.UpdateAvailable)
        {
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateVersionText.Text = $"Version {UpdateService.Instance.LatestVersion} is available";
        }
        else
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;

        try
        {
            await UpdateService.Instance.CheckForUpdatesAsync();
            UpdateUI();

            if (!UpdateService.Instance.UpdateAvailable)
            {
                // Show "up to date" message
                var dialog = new ContentDialog
                {
                    Title = "Up to Date",
                    Content = $"You're running the latest version ({UpdateService.Instance.CurrentVersion}).",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                };
                await dialog.ShowAsync();
            }
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        var url = UpdateService.Instance.DownloadUrl;
        if (!string.IsNullOrEmpty(url))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
