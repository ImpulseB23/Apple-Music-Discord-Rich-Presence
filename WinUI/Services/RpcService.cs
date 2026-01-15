using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using DiscordRPC;
using Newtonsoft.Json.Linq;
using Windows.Media.Control;

namespace AppleMusicRpc.Services;

public class RpcService : IDisposable
{
    private static RpcService? _instance;
    public static RpcService Instance => _instance ??= new RpcService();

    private DiscordRpcClient? _discord;
    private TrackInfo? _lastTrack;
    private TrackInfo? _currentTrack;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, (string? ArtUrl, string? AppleMusicUrl, string? ScrobbleArtist, string? ScrobbleTitle, DateTime AccessTime)> _cache = new();
    private LastFMScrobbler? _scrobbler;
    private CancellationTokenSource? _cts;

    private bool _isPaused;
    private bool _forcePresenceUpdate;
    public bool IsPaused => _isPaused;

    private readonly List<string> _history = new();
    public IReadOnlyList<string> History => _history;

    public TrackInfo? CurrentTrack => _currentTrack;

    private DateTime _lastTrackUpdateTime = DateTime.Now;
    public DateTime LastTrackUpdateTime => _lastTrackUpdateTime;

    public event Action<TrackInfo?>? TrackChanged;
    public event Action<string, bool>? StatusChanged;
    public event Action? HistoryChanged;

    private static readonly string[] AppleMusicIdentifiers = { "applemusic", "appleinc.applemusic", "apple.music", "music.ui" };

    private static readonly string DebugLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiscordRPC", "debug.log");

    private static void DebugLog(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(DebugLogPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.AppendAllText(DebugLogPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    private RpcService()
    {
        DebugLog("=== RpcService starting ===");
        DebugLog($"Looking for Apple Music identifiers: {string.Join(", ", AppleMusicIdentifiers)}");

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.Add("User-Agent", "DiscordRPC/1.0");

        var config = ConfigService.Load();
        InitScrobbler(config);
        Start();
    }

    private void InitScrobbler(AppConfig config)
    {
        if (!string.IsNullOrEmpty(config.LastFmApiKey) && !string.IsNullOrEmpty(config.LastFmApiSecret))
        {
            _scrobbler = new LastFMScrobbler(config.LastFmApiKey, config.LastFmApiSecret, config.ConfigPath);
            if (!string.IsNullOrEmpty(config.LastFmSessionKey))
                _scrobbler.SetSessionKey(config.LastFmSessionKey);
        }
    }

    public void UpdateScrobbler()
    {
        var config = ConfigService.Load();
        _scrobbler?.Dispose();
        _scrobbler = null;
        InitScrobbler(config);
    }

    public void Start()
    {
        DebugLog("Start() called");
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                DebugLog("Task.Run executing");
                await RunAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                DebugLog($"Task.Run CRASH: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        });
        DebugLog("Start() completed");
    }

    public void Pause()
    {
        _isPaused = true;
        try { _discord?.ClearPresence(); } catch { }
        StatusChanged?.Invoke("Paused", false);
    }

    public void Resume()
    {
        _isPaused = false;
        _forcePresenceUpdate = true; // Force immediate presence update
        StatusChanged?.Invoke("Resuming...", false);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        DebugLog("RunAsync started");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                DebugLog("Loop iteration starting");
                if (_discord == null || _discord.IsDisposed)
                {
                    DebugLog("Creating Discord client");
                    _discord = new DiscordRpcClient(Secrets.DiscordClientId);
                    _discord.Initialize();
                    DebugLog("Discord client initialized");
                    StatusChanged?.Invoke("Connected to Discord", false);
                }

                if (_isPaused)
                {
                    await Task.Delay(1000, ct);
                    continue;
                }

                var track = await GetCurrentTrackAsync();
                var trackChanged = _lastTrack == null && track != null ||
                              _lastTrack != null && track == null ||
                              (track != null && _lastTrack != null && (track.Title != _lastTrack.Title || track.Artist != _lastTrack.Artist));

                // Check if we need to force a presence update (e.g., after resume)
                if (_forcePresenceUpdate && track != null)
                {
                    trackChanged = true;
                    _forcePresenceUpdate = false;
                }

                // Always update position even if track didn't change (keeps progress bar accurate)
                if (track != null && _currentTrack != null && !trackChanged)
                {
                    _currentTrack.Position = track.Position;
                    _currentTrack.Duration = track.Duration;
                    _lastTrackUpdateTime = DateTime.Now;
                }

                if (trackChanged)
                {
                    if (track == null)
                    {
                        _discord.ClearPresence();
                        _currentTrack = null;
                        TrackChanged?.Invoke(null);
                        StatusChanged?.Invoke("Stopped", false);
                    }
                    else
                    {
                        AddToHistory($"{track.Artist} — {track.Title}");
                        var (artUrl, appleMusicUrl, scrobbleArtist, scrobbleTitle) = await GetAlbumArtAndUrlAsync(track);
                        track.AlbumArtUrl = artUrl;
                        track.AppleMusicUrl = appleMusicUrl;
                        track.ScrobbleArtist = scrobbleArtist;
                        track.ScrobbleTitle = scrobbleTitle;
                        _currentTrack = track;
                        _lastTrackUpdateTime = DateTime.Now;
                        TrackChanged?.Invoke(track);

                        var presence = new RichPresence
                        {
                            Type = ActivityType.Listening,
                            Details = Truncate(track.Title, 128),
                            State = Truncate(track.Artist, 128),
                            StatusDisplay = StatusDisplayType.State, // Shows artist in sidebar instead of "Apple Music"
                        };

                        if (!string.IsNullOrEmpty(artUrl))
                        {
                            presence.Assets = new Assets
                            {
                                LargeImageKey = artUrl,
                                LargeImageText = track.Album
                            };
                        }

                        if (!string.IsNullOrEmpty(appleMusicUrl))
                        {
                            presence.Buttons = new[] { new Button { Label = "Play on Apple Music", Url = appleMusicUrl } };
                        }

                        if (track.IsPlaying && track.Duration > 0)
                        {
                            var now = DateTimeOffset.UtcNow;
                            presence.Timestamps = new Timestamps
                            {
                                Start = now.AddSeconds(-track.Position).UtcDateTime,
                                End = now.AddSeconds(track.Duration - track.Position).UtcDateTime
                            };
                        }

                        _discord.SetPresence(presence);
                        StatusChanged?.Invoke($"Playing: {Truncate(track.Title, 30)}", false);

                        if (_scrobbler != null && track.IsPlaying)
                        {
                            // Use iTunes names for accurate Last.fm scrobbling, fallback to raw if not found
                            var artistForScrobble = track.ScrobbleArtist ?? track.RawArtist;
                            var titleForScrobble = track.ScrobbleTitle ?? track.RawTitle;
                            await _scrobbler.UpdateNowPlayingAsync(artistForScrobble, titleForScrobble, track.Album, track.Duration);
                        }
                    }
                    _lastTrack = track;
                }

                if (_scrobbler != null && track != null && track.IsPlaying)
                {
                    // Use iTunes names for accurate Last.fm scrobbling, fallback to raw if not found
                    var artistForScrobble = track.ScrobbleArtist ?? _currentTrack?.ScrobbleArtist ?? track.RawArtist;
                    var titleForScrobble = track.ScrobbleTitle ?? _currentTrack?.ScrobbleTitle ?? track.RawTitle;
                    await _scrobbler.CheckAndScrobbleAsync(artistForScrobble, titleForScrobble, track.Album, track.Position);
                }

                // Poll every 2 seconds for accurate progress tracking
                await Task.Delay(2000, ct);
            }
            catch (OperationCanceledException) { DebugLog("Cancelled"); break; }
            catch (Exception ex)
            {
                DebugLog($"ERROR: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                StatusChanged?.Invoke($"Error: {ex.Message}", true);
                try { _discord?.Dispose(); } catch { }
                _discord = null;
                await Task.Delay(10000, ct);
            }
        }
    }

    private void AddToHistory(string entry)
    {
        if (_history.Count > 0 && _history[0] == entry) return;
        _history.Insert(0, entry);
        if (_history.Count > 10) _history.RemoveAt(_history.Count - 1);
        HistoryChanged?.Invoke();
    }

    private async Task<TrackInfo?> GetCurrentTrackAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            GlobalSystemMediaTransportControlsSession? session = null;

            var sessions = manager.GetSessions();
            var sessionIds = new List<string>();

            foreach (var s in sessions)
            {
                var id = s.SourceAppUserModelId.ToLowerInvariant();
                sessionIds.Add(id);
                if (AppleMusicIdentifiers.Any(i => id.Contains(i)))
                {
                    session = s;
                    break;
                }
            }

            // Debug: Log all found sessions
            DebugLog($"Sessions found: {sessions.Count}. IDs: [{string.Join(", ", sessionIds)}]");

            if (session == null)
            {
                if (sessions.Count == 0)
                    StatusChanged?.Invoke("No media sessions found", false);
                else
                    StatusChanged?.Invoke($"Found {sessions.Count} sessions, none are Apple Music", false);
                return null;
            }

            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null) return null;

            var playback = session.GetPlaybackInfo();
            if (playback.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                return null;

            var title = props.Title;
            if (string.IsNullOrWhiteSpace(title) || title == "Unknown") return null;

            var timeline = session.GetTimelineProperties();

            // Get thumbnail from Apple Music
            byte[]? thumb = null;
            try
            {
                var thumbRef = props.Thumbnail;
                if (thumbRef != null)
                {
                    using var stream = await thumbRef.OpenReadAsync();
                    using var ms = new MemoryStream();
                    await stream.AsStreamForRead().CopyToAsync(ms);
                    thumb = ms.ToArray();
                }
            }
            catch { }

            return new TrackInfo
            {
                Title = CleanTitle(title),
                Artist = CleanArtist(props.Artist ?? "Unknown"),
                Album = props.AlbumTitle ?? "",
                RawTitle = title,
                RawArtist = props.Artist ?? "Unknown",
                IsPlaying = true,
                Duration = timeline.EndTime.TotalSeconds,
                Position = timeline.Position.TotalSeconds,
                ThumbnailData = thumb
            };
        }
        catch { return null; }
    }

    private static string CleanTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return title;
        title = Regex.Replace(title, @"\s*-\s*Single$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s*-\s*EP$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s*\[Explicit\]$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s*\(Explicit\)$", "", RegexOptions.IgnoreCase);
        return title.Trim();
    }

    private static string CleanArtist(string artist)
    {
        if (string.IsNullOrEmpty(artist)) return artist;
        var dashIdx = artist.IndexOf(" — ");
        return dashIdx > 0 ? artist[..dashIdx].Trim() : artist.Trim();
    }

    /// <summary>
    /// Cleans artist and title for Last.fm scrobbling when iTunes lookup fails.
    /// Removes common Apple Music metadata that Last.fm doesn't recognize.
    /// </summary>
    private static (string Artist, string Title) CleanForScrobble(string rawArtist, string rawTitle)
    {
        var artist = rawArtist;
        var title = rawTitle;

        // Remove featuring info from artist (e.g., "Drake (feat. Future)" -> "Drake")
        artist = Regex.Replace(artist, @"\s*\(feat\..*?\)", "", RegexOptions.IgnoreCase);
        artist = Regex.Replace(artist, @"\s*(feat\.|featuring|ft\.)\s*.*", "", RegexOptions.IgnoreCase);

        // Remove collaboration marker (e.g., "Drake — Future" -> "Drake")
        var dashIdx = artist.IndexOf(" — ");
        if (dashIdx > 0) artist = artist[..dashIdx];

        // Clean title - remove common suffixes
        title = Regex.Replace(title, @"\s*\(feat\.[^)]*\)", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s*-\s*(Single|EP)$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s*\[(Explicit|Clean)\]$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s*\((Explicit|Clean)\)$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s*-\s*Remastered.*$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s*\(Remastered.*?\)$", "", RegexOptions.IgnoreCase);

        return (artist.Trim(), title.Trim());
    }

    private async Task<(string? ArtUrl, string? AppleMusicUrl, string? ScrobbleArtist, string? ScrobbleTitle)> GetAlbumArtAndUrlAsync(TrackInfo track)
    {
        var cacheKey = $"{track.RawArtist}|{track.RawTitle}".ToLowerInvariant();
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            _cache[cacheKey] = (cached.ArtUrl, cached.AppleMusicUrl, cached.ScrobbleArtist, cached.ScrobbleTitle, DateTime.UtcNow);
            return (cached.ArtUrl, cached.AppleMusicUrl, cached.ScrobbleArtist, cached.ScrobbleTitle);
        }

        string? artUrl = null;
        string? appleMusicUrl = null;
        string? scrobbleArtist = null;
        string? scrobbleTitle = null;

        // Method 1: Upload thumbnail from Apple Music directly (best quality)
        if (track.ThumbnailData != null && track.ThumbnailData.Length > 0)
        {
            try
            {
                var uploadedUrl = await UploadThumbnailAsync(track.ThumbnailData);
                if (!string.IsNullOrEmpty(uploadedUrl))
                {
                    artUrl = uploadedUrl;
                }
            }
            catch { }
        }

        // Method 2: iTunes Search API
        try
        {
            // Use cleaned artist (without album suffix) and cleaned title for better search
            var searchArtist = CleanArtist(track.RawArtist);
            var searchTitle = CleanTitle(track.RawTitle);
            var query = HttpUtility.UrlEncode($"{searchArtist} {searchTitle}");
            var url = $"https://itunes.apple.com/search?term={query}&media=music&entity=song&limit=10";
            var response = await _http.GetStringAsync(url);
            var json = JObject.Parse(response);
            var results = json["results"] as JArray;

            DebugLog($"iTunes search: '{searchArtist} - {searchTitle}' returned {results?.Count ?? 0} results");

            if (results?.Count > 0)
            {
                JToken? bestMatch = null;
                int bestScore = 0;

                foreach (var result in results)
                {
                    var resultArtist = result["artistName"]?.ToString() ?? "";
                    var resultTrack = result["trackName"]?.ToString() ?? "";
                    var resultArtistLower = resultArtist.ToLowerInvariant();
                    var resultTrackLower = resultTrack.ToLowerInvariant();
                    var searchArtistLower = searchArtist.ToLowerInvariant();
                    var searchTitleLower = searchTitle.ToLowerInvariant();

                    int score = 0;

                    // Exact artist match is best
                    if (resultArtistLower == searchArtistLower)
                        score += 100;
                    else if (resultArtistLower.Contains(searchArtistLower) || searchArtistLower.Contains(resultArtistLower))
                        score += 50;

                    // Exact title match is best
                    if (resultTrackLower == searchTitleLower)
                        score += 100;
                    else if (resultTrackLower.Contains(searchTitleLower) || searchTitleLower.Contains(resultTrackLower))
                        score += 50;

                    // Check if title contains remix/version info and it matches
                    if (searchTitleLower.Contains("remix") && resultTrackLower.Contains("remix"))
                        score += 25;
                    if (searchTitleLower.Contains("live") && resultTrackLower.Contains("live"))
                        score += 25;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = result;
                        DebugLog($"  Better match (score {score}): {resultArtist} - {resultTrack}");
                    }
                }

                // Only use the match if we have at least some confidence
                var match = bestScore >= 100 ? bestMatch : (bestMatch ?? results[0]);
                if (bestScore < 100)
                {
                    DebugLog($"  Low confidence match (score {bestScore}), using first result as fallback");
                }

                appleMusicUrl = match!["trackViewUrl"]?.ToString();

                // Extract iTunes artist/title for accurate Last.fm scrobbling
                // iTunes returns the most popular/relevant result first
                scrobbleArtist = match["artistName"]?.ToString();
                scrobbleTitle = match["trackName"]?.ToString();

                if (string.IsNullOrEmpty(artUrl))
                {
                    var itunesArt = match["artworkUrl100"]?.ToString();
                    if (!string.IsNullOrEmpty(itunesArt))
                        artUrl = itunesArt.Replace("100x100bb", "512x512bb");
                }
            }
        }
        catch { }

        // Method 3: Last.FM API fallback
        if (string.IsNullOrEmpty(artUrl))
        {
            try
            {
                var artist = HttpUtility.UrlEncode(track.RawArtist);
                var trackName = HttpUtility.UrlEncode(track.RawTitle);
                var url = $"https://ws.audioscrobbler.com/2.0/?method=track.getInfo&api_key={Secrets.LastFmLookupKey}&artist={artist}&track={trackName}&format=json";
                var response = await _http.GetStringAsync(url);
                var json = JObject.Parse(response);
                var album = json["track"]?["album"];
                if (album != null)
                {
                    var images = album["image"] as JArray;
                    if (images?.Count > 0)
                    {
                        for (int i = images.Count - 1; i >= 0; i--)
                        {
                            var imgUrl = images[i]["#text"]?.ToString();
                            if (!string.IsNullOrEmpty(imgUrl) && !imgUrl.Contains("2a96cbd8b46e442fc41c2b86b821562f"))
                            {
                                artUrl = imgUrl;
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // Fallback: If iTunes lookup didn't find artist/title, search Last.fm for the most popular match
        if (string.IsNullOrEmpty(scrobbleArtist) || string.IsNullOrEmpty(scrobbleTitle))
        {
            try
            {
                // Clean artist and title for search
                var cleanedArtist = CleanArtist(track.RawArtist);
                var cleanedTitle = CleanTitle(track.RawTitle);

                // Extract individual artists (handle collaborations like "Artist1 & Artist2")
                var artists = cleanedArtist.Split(new[] { " & ", ", ", " x ", " X " }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim().ToLowerInvariant())
                    .ToList();

                // Search Last.fm for the track
                var searchQuery = HttpUtility.UrlEncode(cleanedTitle);
                var searchUrl = $"https://ws.audioscrobbler.com/2.0/?method=track.search&api_key={Secrets.LastFmLookupKey}&track={searchQuery}&limit=30&format=json";
                var searchResponse = await _http.GetStringAsync(searchUrl);
                var searchJson = JObject.Parse(searchResponse);
                var tracks = searchJson["results"]?["trackmatches"]?["track"] as JArray;

                if (tracks?.Count > 0)
                {
                    DebugLog($"Last.fm search for '{cleanedTitle}' returned {tracks.Count} results");

                    // Find the best match: artist name contains one of our artists, highest listener count
                    JToken? bestMatch = null;
                    int bestListeners = 0;

                    foreach (var t in tracks)
                    {
                        var resultArtist = t["artist"]?.ToString()?.ToLowerInvariant() ?? "";
                        var resultTitle = t["name"]?.ToString()?.ToLowerInvariant() ?? "";
                        var listeners = t["listeners"]?.Value<int>() ?? 0;

                        // Check if any of our artists match the result artist
                        var artistMatches = artists.Any(a =>
                            resultArtist.Contains(a) || a.Contains(resultArtist));

                        // Check if title matches
                        var titleMatches = resultTitle.Contains(cleanedTitle.ToLowerInvariant()) ||
                                          cleanedTitle.ToLowerInvariant().Contains(resultTitle);

                        if (artistMatches && titleMatches && listeners > bestListeners)
                        {
                            bestListeners = listeners;
                            bestMatch = t;
                            DebugLog($"  Better Last.fm match ({listeners:N0} listeners): {t["artist"]} - {t["name"]}");
                        }
                    }

                    if (bestMatch != null)
                    {
                        scrobbleArtist = bestMatch["artist"]?.ToString();
                        scrobbleTitle = bestMatch["name"]?.ToString();
                        DebugLog($"Using Last.fm match: {scrobbleArtist} - {scrobbleTitle} ({bestListeners:N0} listeners)");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Last.fm search fallback failed: {ex.Message}");
            }

            // Ultimate fallback: use cleaned values if Last.fm search also failed
            if (string.IsNullOrEmpty(scrobbleArtist) || string.IsNullOrEmpty(scrobbleTitle))
            {
                var cleaned = CleanForScrobble(track.RawArtist, track.RawTitle);
                scrobbleArtist ??= cleaned.Artist;
                scrobbleTitle ??= cleaned.Title;
                DebugLog($"All lookups failed, using cleaned: {scrobbleArtist} - {scrobbleTitle}");
            }
        }

        // Fallback Apple Music URL: construct a search link if iTunes didn't find a direct link
        if (string.IsNullOrEmpty(appleMusicUrl))
        {
            var searchArtist = CleanArtist(track.RawArtist);
            var searchTitle = CleanTitle(track.RawTitle);
            var searchTerm = HttpUtility.UrlEncode($"{searchArtist} {searchTitle}");
            appleMusicUrl = $"https://music.apple.com/search?term={searchTerm}";
            DebugLog($"Using Apple Music search URL fallback");
        }

        // Cache cleanup
        if (_cache.Count > 100)
        {
            var oldest = _cache.OrderBy(x => x.Value.AccessTime).First().Key;
            _cache.TryRemove(oldest, out _);
        }
        _cache[cacheKey] = (artUrl, appleMusicUrl, scrobbleArtist, scrobbleTitle, DateTime.UtcNow);

        return (artUrl, appleMusicUrl, scrobbleArtist, scrobbleTitle);
    }

    private async Task<string?> UploadThumbnailAsync(byte[] imageData)
    {
        // Method 1: catbox.moe (permanent, reliable)
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent("fileupload"), "reqtype");
            content.Add(new ByteArrayContent(imageData), "fileToUpload", "cover.jpg");

            using var response = await _http.PostAsync("https://catbox.moe/user/api.php", content);
            if (response.IsSuccessStatusCode)
            {
                var url = (await response.Content.ReadAsStringAsync()).Trim();
                if (url.StartsWith("https://files.catbox.moe/"))
                    return url;
            }
        }
        catch { }

        // Method 2: 0x0.st
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(imageData), "file", "cover.jpg");

            using var response = await _http.PostAsync("https://0x0.st", content);
            if (response.IsSuccessStatusCode)
            {
                var url = (await response.Content.ReadAsStringAsync()).Trim();
                if (url.StartsWith("https://0x0.st/") || url.StartsWith("http://0x0.st/"))
                    return url.Replace("http://", "https://");
            }
        }
        catch { }

        // Method 3: file.io
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(imageData), "file", "cover.jpg");

            using var response = await _http.PostAsync("https://file.io", content);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var parsed = JObject.Parse(json);
                if (parsed["success"]?.Value<bool>() == true)
                {
                    var url = parsed["link"]?.ToString();
                    if (!string.IsNullOrEmpty(url))
                        return url;
                }
            }
        }
        catch { }

        return null;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _discord?.ClearPresence(); } catch { }
        try { _discord?.Dispose(); } catch { }
        _http.Dispose();
        _scrobbler?.Dispose();
    }
}
