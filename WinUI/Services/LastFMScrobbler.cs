using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AppleMusicRpc.Services
{
    public class LastFMScrobbler : IDisposable
    {
        private readonly string _apiKey;
        private readonly string _apiSecret;
        private readonly string _configPath;
        private readonly HttpClient _http;
        
        private string? _sessionKey;
        private string? _username;
        
        // Track scrobbling state
        private string? _currentTrackKey;
        private DateTime _trackStartTime;
        private double _trackDuration;
        private bool _hasScrobbled;
        
        private const string ApiUrl = "https://ws.audioscrobbler.com/2.0/";

        private static readonly string DebugLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AppleMusicDiscordRPC", "debug.log");

        public bool IsAuthenticated => !string.IsNullOrEmpty(_sessionKey);
        public string? Username => _username;
        public string ApiKey => _apiKey;
        public string ApiSecret => _apiSecret;

        public LastFMScrobbler(string apiKey, string apiSecret, string configPath)
        {
            _apiKey = apiKey;
            _apiSecret = apiSecret;
            _configPath = configPath;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        private void Log(string msg)
        {
            try
            {
                File.AppendAllText(DebugLog, $"[{DateTime.Now:HH:mm:ss}] [LASTFM] {msg}\n");
            }
            catch
            {
                // Logging failed
            }
        }

        public void SetSessionKey(string sessionKey)
        {
            _sessionKey = sessionKey;
        }

        /// <summary>
        /// Full authentication flow - opens browser and polls until authorized
        /// </summary>
        public async Task<bool> AuthenticateAsync()
        {
            try
            {
                // Step 1: Get a token
                var tokenParams = new Dictionary<string, string>
                {
                    ["method"] = "auth.getToken",
                    ["api_key"] = _apiKey,
                    ["format"] = "json"
                };
                
                var response = await _http.GetStringAsync($"{ApiUrl}?{BuildQuery(tokenParams)}");
                var json = JObject.Parse(response);
                var token = json["token"]?.ToString();
                
                if (string.IsNullOrEmpty(token))
                {
                    return false;
                }
                
                // Step 2: Open browser for user authorization
                var authUrl = $"https://www.last.fm/api/auth/?api_key={_apiKey}&token={token}";
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
                
                // Step 3: Poll for authorization (check every 2 seconds for up to 2 minutes)
                for (int i = 0; i < 60; i++)
                {
                    await Task.Delay(2000);
                    
                    if (await TryGetSessionAsync(token))
                    {
                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Log($"AuthenticateAsync error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TryGetSessionAsync(string token)
        {
            try
            {
                var sessionParams = new Dictionary<string, string>
                {
                    ["method"] = "auth.getSession",
                    ["api_key"] = _apiKey,
                    ["token"] = token
                };
                
                var sig = GenerateSignature(sessionParams);
                sessionParams["api_sig"] = sig;
                sessionParams["format"] = "json";
                
                var response = await _http.GetStringAsync($"{ApiUrl}?{BuildQuery(sessionParams)}");
                var json = JObject.Parse(response);
                
                var session = json["session"];
                if (session == null)
                {
                    return false;
                }
                
                _sessionKey = session["key"]?.ToString();
                _username = session["name"]?.ToString();
                
                if (string.IsNullOrEmpty(_sessionKey))
                {
                    return false;
                }
                
                // Save session key to config
                SaveSessionKeyToConfig();

                return true;
            }
            catch (Exception ex)
            {
                Log($"TryGetSessionAsync error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Update Now Playing status on Last.FM
        /// </summary>
        public async Task UpdateNowPlayingAsync(string artist, string track, string? album = null, double? duration = null)
        {
            if (!IsAuthenticated)
            {
                return;
            }

            try
            {
                var trackKey = $"{artist}|{track}";
                
                // Reset scrobble state for new track
                if (trackKey != _currentTrackKey)
                {
                    _currentTrackKey = trackKey;
                    _trackStartTime = DateTime.UtcNow;
                    _trackDuration = duration ?? 0;
                    _hasScrobbled = false;
                }

                var parameters = new Dictionary<string, string>
                {
                    ["method"] = "track.updateNowPlaying",
                    ["api_key"] = _apiKey,
                    ["sk"] = _sessionKey!,
                    ["artist"] = artist,
                    ["track"] = track
                };

                if (!string.IsNullOrEmpty(album))
                    parameters["album"] = album;
                
                if (duration.HasValue && duration.Value > 0)
                    parameters["duration"] = ((int)duration.Value).ToString();

                var sig = GenerateSignature(parameters);
                parameters["api_sig"] = sig;
                parameters["format"] = "json";

                var content = new FormUrlEncodedContent(parameters);
                var response = await _http.PostAsync(ApiUrl, content);
                var responseText = await response.Content.ReadAsStringAsync();

                // Validate response and log errors
                var json = JObject.Parse(responseText);
                if (json["error"] != null)
                {
                    var errorCode = json["error"]?.Value<int>();
                    var errorMessage = json["message"]?.ToString();
                    Log($"UpdateNowPlaying error {errorCode}: {errorMessage} (artist: {artist}, track: {track})");
                }
                else
                {
                    Log($"Now playing: {artist} - {track}");
                }
            }
            catch (Exception ex)
            {
                Log($"UpdateNowPlayingAsync error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if track should be scrobbled and do it
        /// </summary>
        public async Task CheckAndScrobbleAsync(string artist, string track, string? album = null, double position = 0)
        {
            if (!IsAuthenticated || _hasScrobbled)
                return;

            var trackKey = $"{artist}|{track}";
            if (trackKey != _currentTrackKey)
                return;

            // Last.FM rules: scrobble after 50% of track OR 4 minutes, whichever comes first
            var elapsed = (DateTime.UtcNow - _trackStartTime).TotalSeconds;
            var shouldScrobble = false;

            if (_trackDuration > 30)
            {
                var halfDuration = _trackDuration / 2;
                var scrobblePoint = Math.Min(halfDuration, 240);
                
                if (elapsed >= scrobblePoint || position >= scrobblePoint)
                {
                    shouldScrobble = true;
                }
            }
            else if (elapsed >= 30)
            {
                shouldScrobble = true;
            }

            if (shouldScrobble)
            {
                await ScrobbleAsync(artist, track, album);
            }
        }

        private async Task ScrobbleAsync(string artist, string track, string? album = null)
        {
            if (!IsAuthenticated || _hasScrobbled)
                return;

            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                
                var parameters = new Dictionary<string, string>
                {
                    ["method"] = "track.scrobble",
                    ["api_key"] = _apiKey,
                    ["sk"] = _sessionKey!,
                    ["artist"] = artist,
                    ["track"] = track,
                    ["timestamp"] = timestamp.ToString()
                };

                if (!string.IsNullOrEmpty(album))
                    parameters["album"] = album;

                var sig = GenerateSignature(parameters);
                parameters["api_sig"] = sig;
                parameters["format"] = "json";

                var content = new FormUrlEncodedContent(parameters);
                var response = await _http.PostAsync(ApiUrl, content);
                var responseText = await response.Content.ReadAsStringAsync();
                
                var json = JObject.Parse(responseText);

                // Check for API errors
                if (json["error"] != null)
                {
                    var errorCode = json["error"]?.Value<int>();
                    var errorMessage = json["message"]?.ToString();
                    Log($"Scrobble error {errorCode}: {errorMessage} (artist: {artist}, track: {track})");
                    return;
                }

                // Check if scrobble was accepted
                var accepted = json["scrobbles"]?["@attr"]?["accepted"]?.Value<int>() ?? 0;
                var ignored = json["scrobbles"]?["@attr"]?["ignored"]?.Value<int>() ?? 0;

                if (accepted > 0)
                {
                    _hasScrobbled = true;
                    StatisticsService.Instance.IncrementScrobbleCount();
                    Log($"Scrobbled: {artist} - {track}");
                }
                else if (ignored > 0)
                {
                    // Get ignore reason if available
                    var ignoreCode = json["scrobbles"]?["scrobble"]?["ignoredMessage"]?["code"]?.ToString();
                    var ignoreText = json["scrobbles"]?["scrobble"]?["ignoredMessage"]?["#text"]?.ToString();
                    Log($"Scrobble ignored ({ignoreCode}): {ignoreText} (artist: {artist}, track: {track})");
                    _hasScrobbled = true; // Don't retry ignored scrobbles
                }
                else
                {
                    Log($"Scrobble response unclear: {responseText}");
                }
            }
            catch (Exception ex)
            {
                Log($"ScrobbleAsync error: {ex.Message}");
            }
        }

        private string GenerateSignature(Dictionary<string, string> parameters)
        {
            var sorted = parameters.OrderBy(p => p.Key);
            var sb = new StringBuilder();
            
            foreach (var param in sorted)
            {
                sb.Append(param.Key);
                sb.Append(param.Value);
            }
            
            sb.Append(_apiSecret);
            
            using var md5 = MD5.Create();
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var hash = md5.ComputeHash(bytes);
            
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private string BuildQuery(Dictionary<string, string> parameters)
        {
            return string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        }

        private void SaveSessionKeyToConfig()
        {
            try
            {
                if (!File.Exists(_configPath))
                    return;

                var lines = File.ReadAllLines(_configPath).ToList();
                var updated = false;

                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].TrimStart().StartsWith("session_key"))
                    {
                        lines[i] = $"session_key = {_sessionKey}";
                        updated = true;
                        break;
                    }
                }

                if (updated)
                {
                    File.WriteAllLines(_configPath, lines);
                    Log("Session key saved to config");
                }
            }
            catch (Exception ex)
            {
                Log($"SaveSessionKeyToConfig error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}