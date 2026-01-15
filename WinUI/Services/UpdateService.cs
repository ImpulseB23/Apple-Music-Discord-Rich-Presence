using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace AppleMusicRpc.Services;

public class UpdateService
{
    private static readonly Lazy<UpdateService> _instance = new(() => new UpdateService());
    public static UpdateService Instance => _instance.Value;

    private readonly HttpClient _http;
    private const string GitHubApiUrl = "https://api.github.com/repos/ImpulseB23/Apple-Music-Discord-Rich-Presence/releases/latest";

    public string CurrentVersion { get; }
    public string? LatestVersion { get; private set; }
    public string? DownloadUrl { get; private set; }
    public bool UpdateAvailable { get; private set; }

    public event Action<string, string>? UpdateFound; // (currentVersion, latestVersion)

    private UpdateService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "AppleMusicDiscordRPC");

        // Get current version from assembly
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
    }

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            var response = await _http.GetStringAsync(GitHubApiUrl);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // Get tag name (e.g., "v1.0.1" or "1.0.1")
            var tagName = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrEmpty(tagName)) return;

            // Clean version string (remove 'v' prefix if present)
            LatestVersion = tagName.TrimStart('v', 'V');

            // Get download URL for the installer
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        DownloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            // Fallback to release page
            if (string.IsNullOrEmpty(DownloadUrl))
            {
                DownloadUrl = root.GetProperty("html_url").GetString();
            }

            // Compare versions
            if (IsNewerVersion(LatestVersion, CurrentVersion))
            {
                UpdateAvailable = true;
                UpdateFound?.Invoke(CurrentVersion, LatestVersion);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
        }
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        try
        {
            var latestParts = latest.Split('.');
            var currentParts = current.Split('.');

            for (int i = 0; i < Math.Max(latestParts.Length, currentParts.Length); i++)
            {
                var latestNum = i < latestParts.Length && int.TryParse(latestParts[i], out var l) ? l : 0;
                var currentNum = i < currentParts.Length && int.TryParse(currentParts[i], out var c) ? c : 0;

                if (latestNum > currentNum) return true;
                if (latestNum < currentNum) return false;
            }
        }
        catch { }

        return false;
    }
}
