using System;
using System.Collections.Generic;
using System.Linq;

namespace AppleMusicRpc.Services;

public class StatisticsService
{
    private static StatisticsService? _instance;
    public static StatisticsService Instance => _instance ??= new StatisticsService();

    public DateTime SessionStart { get; }
    public int TrackCount { get; private set; }
    public double TotalListenTime { get; private set; }
    public int ScrobbleCount { get; private set; }
    public IReadOnlyDictionary<string, int> ArtistCounts => _artistCounts;

    private readonly Dictionary<string, int> _artistCounts = new();

    public event Action? StatsUpdated;

    private StatisticsService()
    {
        SessionStart = DateTime.Now;

        // Subscribe to RpcService track changes
        RpcService.Instance.TrackChanged += OnTrackChanged;

        // Initialize from history
        InitializeFromHistory();
    }

    private void InitializeFromHistory()
    {
        TrackCount = RpcService.Instance.History.Count;
        foreach (var entry in RpcService.Instance.History)
        {
            var parts = entry.Split(" — ");
            if (parts.Length > 0)
            {
                var artist = parts[0].Trim();
                _artistCounts[artist] = _artistCounts.GetValueOrDefault(artist) + 1;
            }
        }
    }

    private void OnTrackChanged(TrackInfo? track)
    {
        if (track == null) return;

        TrackCount++;
        TotalListenTime += track.Duration;

        if (!string.IsNullOrEmpty(track.Artist))
        {
            _artistCounts[track.Artist] = _artistCounts.GetValueOrDefault(track.Artist) + 1;
        }

        StatsUpdated?.Invoke();
    }

    public void IncrementScrobbleCount()
    {
        ScrobbleCount++;
        StatsUpdated?.Invoke();
    }

    public IEnumerable<KeyValuePair<string, int>> GetTopArtists(int count = 5)
    {
        return _artistCounts
            .OrderByDescending(x => x.Value)
            .Take(count);
    }

    public static string FormatDuration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{ts.Minutes}m";
    }
}
