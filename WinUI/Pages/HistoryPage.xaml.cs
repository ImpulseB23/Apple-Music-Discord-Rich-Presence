using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.ObjectModel;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using AppleMusicRpc.Services;

namespace AppleMusicRpc.Pages;

public sealed partial class HistoryPage : Page
{
    private readonly ObservableCollection<string> _history = new();
    private readonly RpcService _rpc;

    public HistoryPage()
    {
        InitializeComponent();
        _rpc = RpcService.Instance;
        HistoryList.ItemsSource = _history;

        // Load initial history
        foreach (var item in _rpc.History)
        {
            _history.Add(item);
        }
        UpdateVisibility();

        // Subscribe to changes
        _rpc.HistoryChanged += OnHistoryChanged;
    }

    private void OnHistoryChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _history.Clear();
            foreach (var item in _rpc.History)
            {
                _history.Add(item);
            }
            UpdateVisibility();
        });
    }

    private void UpdateVisibility()
    {
        var hasHistory = _history.Count > 0;
        EmptyState.Visibility = hasHistory ? Visibility.Collapsed : Visibility.Visible;
        HistoryList.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
        ActionButtons.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CopyAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Count == 0) return;

        // Format all songs nicely
        var sb = new StringBuilder();
        sb.AppendLine("Recently Played:");
        sb.AppendLine();
        for (int i = 0; i < _history.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {_history[i]}");
        }

        var dataPackage = new DataPackage();
        dataPackage.SetText(sb.ToString().TrimEnd());
        Clipboard.SetContent(dataPackage);
    }

    private void CopySelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is string selected)
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(selected);
            Clipboard.SetContent(dataPackage);
        }
    }

    private void CopyItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string text)
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        UpdateVisibility();
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is string selected)
        {
            var query = Uri.EscapeDataString(selected);
            Windows.System.Launcher.LaunchUriAsync(new Uri($"https://www.google.com/search?q={query}"));
        }
    }

    private void HistoryList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        CopySelectedButton_Click(sender, e);
    }

    private void HistoryList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element)
        {
            ContextMenu.ShowAt(element, e.GetPosition(element));
        }
    }

    private void HistoryItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            // Find the copy button and show it
            foreach (var child in grid.Children)
            {
                if (child is Button button)
                {
                    button.Opacity = 1;
                }
            }
        }
    }

    private void HistoryItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            // Find the copy button and hide it
            foreach (var child in grid.Children)
            {
                if (child is Button button)
                {
                    button.Opacity = 0;
                }
            }
        }
    }
}
