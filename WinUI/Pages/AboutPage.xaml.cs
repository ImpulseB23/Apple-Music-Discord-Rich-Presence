using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;

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
    }
}
