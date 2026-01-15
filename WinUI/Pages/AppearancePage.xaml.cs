using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AppleMusicRpc.Services;

namespace AppleMusicRpc.Pages;

public sealed partial class AppearancePage : Page
{
    private readonly AppConfig _config;
    private bool _isLoading = true;

    public AppearancePage()
    {
        InitializeComponent();
        _config = ConfigService.Load();
        LoadSettings();
        _isLoading = false;
    }

    private void LoadSettings()
    {
        // Load theme
        var root = App.MainWindow?.Content as FrameworkElement;
        if (root != null)
        {
            switch (root.RequestedTheme)
            {
                case ElementTheme.Light:
                    LightTheme.IsChecked = true;
                    break;
                case ElementTheme.Dark:
                    DarkTheme.IsChecked = true;
                    break;
                default:
                    SystemTheme.IsChecked = true;
                    break;
            }
        }

        // Load appearance settings
        AlwaysOnTopToggle.IsOn = _config.AlwaysOnTop;
        CompactModeToggle.IsOn = _config.StartInMiniMode;
        ExpandedSidebarToggle.IsOn = _config.StartWithExpandedSidebar;
        MinimizeToTrayToggle.IsOn = _config.MinimizeToTrayOnClose;
        StartMinimizedToggle.IsOn = _config.StartMinimizedToTray;
        CheckUpdatesToggle.IsOn = _config.CheckForUpdatesAutomatically;
    }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeSelector.SelectedItem is RadioButton selected)
        {
            var theme = selected.Tag?.ToString() switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            var root = App.MainWindow?.Content as FrameworkElement;
            if (root != null)
            {
                root.RequestedTheme = theme;
            }
        }
    }

    private void AlwaysOnTop_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        _config.AlwaysOnTop = AlwaysOnTopToggle.IsOn;
        ConfigService.Save(_config);

        // Apply immediately to current window
        App.MainWindow?.SetAlwaysOnTop(AlwaysOnTopToggle.IsOn);
    }

    private void CompactMode_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        _config.StartInMiniMode = CompactModeToggle.IsOn;
        ConfigService.Save(_config);
    }

    private void ExpandedSidebar_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        _config.StartWithExpandedSidebar = ExpandedSidebarToggle.IsOn;
        ConfigService.Save(_config);
    }

    private void MinimizeToTray_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        _config.MinimizeToTrayOnClose = MinimizeToTrayToggle.IsOn;
        ConfigService.Save(_config);
    }

    private void StartMinimized_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        _config.StartMinimizedToTray = StartMinimizedToggle.IsOn;
        ConfigService.Save(_config);
    }

    private void CheckUpdates_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        _config.CheckForUpdatesAutomatically = CheckUpdatesToggle.IsOn;
        ConfigService.Save(_config);
    }
}
