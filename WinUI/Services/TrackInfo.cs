namespace AppleMusicRpc.Services;

public class TrackInfo
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string RawTitle { get; set; } = "";
    public string RawArtist { get; set; } = "";
    public bool IsPlaying { get; set; }
    public double Duration { get; set; }
    public double Position { get; set; }
    public byte[]? ThumbnailData { get; set; }
    public string? AlbumArtUrl { get; set; }
    public string? AppleMusicUrl { get; set; }

    // iTunes-normalized names for accurate Last.fm scrobbling
    public string? ScrobbleArtist { get; set; }
    public string? ScrobbleTitle { get; set; }
}
