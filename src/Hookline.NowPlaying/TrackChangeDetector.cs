using Hookline.NowPlaying.SystemMedia;

namespace Hookline.NowPlaying;

internal static class TrackChangeDetector
{
    private static readonly TimeSpan RestartWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinimumRollback = TimeSpan.FromSeconds(5);

    public static bool IsReplay(
        SystemTimelineProperties? previous,
        SystemTimelineProperties current,
        TimeSpan? duration,
        PlaybackState playbackState
    )
    {
        if (previous is null || current.Position > RestartWindow)
        {
            return false;
        }

        var projectedPreviousPosition = previous.Position;
        if (
            playbackState == PlaybackState.Playing
            && current.LastUpdatedTime > previous.LastUpdatedTime
        )
        {
            projectedPreviousPosition +=
                current.LastUpdatedTime - previous.LastUpdatedTime;
        }

        if (duration is { } knownDuration)
        {
            projectedPreviousPosition = TimeSpan.FromTicks(
                Math.Min(projectedPreviousPosition.Ticks, knownDuration.Ticks)
            );
        }

        return projectedPreviousPosition - current.Position >= MinimumRollback;
    }

    public static bool IsLikelyAd(string title) =>
        string.Equals(title.Trim(), "Advertisement", StringComparison.OrdinalIgnoreCase);
}
