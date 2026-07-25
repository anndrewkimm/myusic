namespace Hookline.NowPlaying;

/// <summary>
/// A timestamped position in the current media item's playback timeline.
/// </summary>
public sealed record PlaybackTimelineSnapshot
{
    public required TimeSpan Position { get; init; }

    public required DateTimeOffset LastUpdatedTime { get; init; }

    public TimeSpan EstimatePositionAt(
        DateTimeOffset timestamp,
        PlaybackState playbackState
    )
    {
        if (
            playbackState != PlaybackState.Playing
            || timestamp <= LastUpdatedTime
        )
        {
            return Position;
        }

        return Position + (timestamp - LastUpdatedTime);
    }
}
