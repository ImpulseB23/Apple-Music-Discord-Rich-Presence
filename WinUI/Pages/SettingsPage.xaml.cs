using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using AppleMusicRpc.Services;

namespace AppleMusicRpc.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly AppConfig _config;

    public SettingsPage()
    {
        InitializeComponent();
        _config = ConfigService.Load();
        LoadSettings();
    }

    private void LoadSettings()
    {
        ApiKeyInput.Text = _config.LastFmApiKey;
        ApiSecretInput.Password = _config.LastFmApiSecret;
        ScrobbleToggle.IsOn = _config.LastFmScrobblingEnabled;
        StartupToggle.IsOn = IsInStartup();
        UpdateConnectionStatus();
    }

    private void UpdateConnectionStatus()
    {
        if (!string.IsNullOrEmpty(_config.LastFmSessionKey))
        {
            ConnectionStatus.Text = "Connected";
            ConnectionDot.Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 108, 203, 95));
        }
        else
        {
            ConnectionStatus.Text = "Not connected";
            ConnectionDot.Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 140, 140, 140));
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ApiKeyInput.Text) || string.IsNullOrWhiteSpace(ApiSecretInput.Password))
        {
            await ShowDialog("Missing", "Enter API Key and Secret first");
            return;
        }

        _config.LastFmApiKey = ApiKeyInput.Text.Trim();
        _config.LastFmApiSecret = ApiSecretInput.Password.Trim();

        ConnectButton.IsEnabled = false;
        ConnectionStatus.Text = "Connecting...";
        ConnectionDot.Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 252, 185, 0));

        try
        {
            var scrobbler = new LastFMScrobbler(_config.LastFmApiKey, _config.LastFmApiSecret, _config.ConfigPath);
            var success = await scrobbler.AuthenticateAsync();

            if (success)
            {
                _config.LastFmSessionKey = ConfigService.Load().LastFmSessionKey;
                UpdateConnectionStatus();
            }
            else
            {
                ConnectionStatus.Text = "Failed";
                ConnectionDot.Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 232, 17, 35));
            }
        }
        catch
        {
            ConnectionStatus.Text = "Error";
            ConnectionDot.Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 232, 17, 35));
        }

        ConnectButton.IsEnabled = true;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _config.LastFmApiKey = ApiKeyInput.Text.Trim();
        _config.LastFmApiSecret = ApiSecretInput.Password.Trim();
        _config.LastFmScrobblingEnabled = ScrobbleToggle.IsOn;

        ConfigService.Save(_config);
        RpcService.Instance.UpdateScrobbler();

        await ShowDialog("Saved", "Settings saved successfully!");
    }

    private void SetupHelpButton_Click(object sender, RoutedEventArgs e)
    {
        SetupGuidePanel.Visibility = SetupGuidePanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            var exePath = Environment.ProcessPath;
            if (StartupToggle.IsOn && exePath != null)
                key.SetValue("DiscordRPC", $"\"{exePath}\"");
            else
                key.DeleteValue("DiscordRPC", false);
        }
        catch { }
    }

    private bool IsInStartup()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("DiscordRPC") != null;
        }
        catch { return false; }
    }

    private async Task ShowDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    // Export handlers
    private async void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var history = RpcService.Instance.History.ToList();
            if (history.Count == 0)
            {
                await ShowDialog("Export", "No history to export.");
                return;
            }

            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("JSON File", new[] { ".json" });
            picker.SuggestedFileName = $"listening_history_{DateTime.Now:yyyyMMdd}";

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(file.Path, json);
                await ShowDialog("Export", $"Exported {history.Count} tracks to JSON.");
            }
        }
        catch (Exception ex)
        {
            await ShowDialog("Error", $"Export failed: {ex.Message}");
        }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var history = RpcService.Instance.History.ToList();
            if (history.Count == 0)
            {
                await ShowDialog("Export", "No history to export.");
                return;
            }

            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("CSV File", new[] { ".csv" });
            picker.SuggestedFileName = $"listening_history_{DateTime.Now:yyyyMMdd}";

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                var csv = "Artist,Title\n" + string.Join("\n", history.Select(h =>
                {
                    var parts = h.Split(" — ");
                    var artist = parts.Length > 0 ? parts[0].Replace("\"", "\"\"") : "";
                    var title = parts.Length > 1 ? parts[1].Replace("\"", "\"\"") : "";
                    return $"\"{artist}\",\"{title}\"";
                }));
                await File.WriteAllTextAsync(file.Path, csv);
                await ShowDialog("Export", $"Exported {history.Count} tracks to CSV.");
            }
        }
        catch (Exception ex)
        {
            await ShowDialog("Error", $"Export failed: {ex.Message}");
        }
    }

    // Logs handlers
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiscordRPC", "debug.log");

    private async void ViewLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(LogPath))
            {
                var content = await File.ReadAllTextAsync(LogPath);
                var lines = content.Split('\n').TakeLast(100);
                var dialog = new ContentDialog
                {
                    Title = "Debug Logs (Last 100 lines)",
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            Text = string.Join("\n", lines),
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                            TextWrapping = TextWrapping.Wrap,
                            IsTextSelectionEnabled = true
                        },
                        MaxHeight = 400
                    },
                    CloseButtonText = "Close",
                    XamlRoot = XamlRoot
                };
                await dialog.ShowAsync();
            }
            else
            {
                await ShowDialog("Logs", "No log file found.");
            }
        }
        catch (Exception ex)
        {
            await ShowDialog("Error", $"Failed to read logs: {ex.Message}");
        }
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.GetDirectoryName(LogPath);
            if (folder != null && Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private async void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(LogPath))
            {
                File.Delete(LogPath);
                await ShowDialog("Logs", "Log file cleared.");
            }
            else
            {
                await ShowDialog("Logs", "No log file to clear.");
            }
        }
        catch (Exception ex)
        {
            await ShowDialog("Error", $"Failed to clear logs: {ex.Message}");
        }
    }
}
