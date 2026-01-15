namespace AppleMusicRpc.Services;

/// <summary>
/// Copy this file to Secrets.cs and fill in your own values.
///
/// To get these values:
/// 1. Discord Client ID: Create an app at https://discord.com/developers/applications
///    - Create New Application -> Copy "Application ID"
/// 2. Last.fm API Key: Get from https://www.last.fm/api/account/create
///    - This is only used for album art lookup fallback
/// </summary>
public static class Secrets
{
    // Your Discord Application ID from https://discord.com/developers/applications
    public const string DiscordClientId = "YOUR_DISCORD_CLIENT_ID_HERE";

    // Last.fm API key for album art fallback (optional but recommended)
    public const string LastFmLookupKey = "YOUR_LASTFM_API_KEY_HERE";
}
