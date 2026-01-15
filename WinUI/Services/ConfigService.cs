using System;
using System.IO;

namespace AppleMusicRpc.Services;

public class AppConfig
{
    public string LastFmApiKey { get; set; } = "";
    public string LastFmApiSecret { get; set; } = "";
    public bool LastFmScrobblingEnabled { get; set; }
    public string LastFmSessionKey { get; set; } = "";
    public string? LastFmLookupKey { get; set; }
    public string ConfigPath { get; set; } = "";

    // Appearance settings
    public bool AlwaysOnTop { get; set; }
    public bool StartInMiniMode { get; set; }
    public bool StartWithExpandedSidebar { get; set; }

    // Hotkey settings (format: "Ctrl+Alt+M")
    public string HotkeyMiniMode { get; set; } = "Ctrl+Alt+M";
    public string HotkeyPauseRpc { get; set; } = "Ctrl+Alt+P";
}

public static class ConfigService
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiscordRPC", "config.ini");

    public static AppConfig Load()
    {
        var cfg = new AppConfig { ConfigPath = DefaultPath };

        Directory.CreateDirectory(Path.GetDirectoryName(DefaultPath)!);

        if (!File.Exists(DefaultPath)) return cfg;

        try
        {
            foreach (var line in File.ReadAllLines(DefaultPath))
            {
                var t = line.Trim();
                if (t.StartsWith("#") || !t.Contains('=')) continue;
                var p = t.Split('=', 2);
                var k = p[0].Trim().ToLower();
                var v = p[1].Trim();

                switch (k)
                {
                    case "api_key": cfg.LastFmApiKey = v; break;
                    case "api_secret": cfg.LastFmApiSecret = v; break;
                    case "enable_scrobbling": cfg.LastFmScrobblingEnabled = v.ToLower() == "true"; break;
                    case "session_key": cfg.LastFmSessionKey = v; break;
                    case "lastfm_lookup_key": cfg.LastFmLookupKey = v; break;
                    case "always_on_top": cfg.AlwaysOnTop = v.ToLower() == "true"; break;
                    case "start_in_mini_mode": cfg.StartInMiniMode = v.ToLower() == "true"; break;
                    case "start_expanded_sidebar": cfg.StartWithExpandedSidebar = v.ToLower() == "true"; break;
                    case "hotkey_mini_mode": cfg.HotkeyMiniMode = v; break;
                    case "hotkey_pause_rpc": cfg.HotkeyPauseRpc = v; break;
                }
            }
        }
        catch { }

        return cfg;
    }

    public static void Save(AppConfig cfg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cfg.ConfigPath)!);
            File.WriteAllLines(cfg.ConfigPath, new[]
            {
                "[LastFM]",
                $"api_key = {cfg.LastFmApiKey}",
                $"api_secret = {cfg.LastFmApiSecret}",
                $"enable_scrobbling = {cfg.LastFmScrobblingEnabled.ToString().ToLower()}",
                $"session_key = {cfg.LastFmSessionKey}",
                "",
                "[General]",
                $"lastfm_lookup_key = {cfg.LastFmLookupKey ?? "6c2d7293c3a0299a9852f77b88ba167e"}",
                "",
                "[Appearance]",
                $"always_on_top = {cfg.AlwaysOnTop.ToString().ToLower()}",
                $"start_in_mini_mode = {cfg.StartInMiniMode.ToString().ToLower()}",
                $"start_expanded_sidebar = {cfg.StartWithExpandedSidebar.ToString().ToLower()}",
                "",
                "[Hotkeys]",
                $"hotkey_mini_mode = {cfg.HotkeyMiniMode}",
                $"hotkey_pause_rpc = {cfg.HotkeyPauseRpc}"
            });
        }
        catch { }
    }
}
