namespace Hookline.NowPlaying;

/// <summary>
/// Metadata for one distinct play of a media item.
/// </summary>
public sealed record NowPlayingTrack
{
    public required long InstanceId { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    public required string Album { get; init; }

    public TimeSpan? Duration { get; init; }

    public ReadOnlyMemory<byte> AlbumArt { get; init; }

    public bool IsLikelyAd { get; init; }
}
