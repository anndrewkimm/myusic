namespace Hookline.NowPlaying;

public sealed class TrackChangedEventArgs(NowPlayingTrack track) : EventArgs
{
    public NowPlayingTrack Track { get; } = track;
}

public sealed class PlaybackStateChangedEventArgs(
    PlaybackState previousState,
    PlaybackState currentState
) : EventArgs
{
    public PlaybackState PreviousState { get; } = previousState;

    public PlaybackState CurrentState { get; } = currentState;
}

public sealed class WatcherStatusChangedEventArgs(
    NowPlayingWatcherStatus status,
    string? detail = null
) : EventArgs
{
    public NowPlayingWatcherStatus Status { get; } = status;

    public string? Detail { get; } = detail;
}

public sealed class PlaybackTimelineChangedEventArgs(
    PlaybackTimelineSnapshot? timeline
) : EventArgs
{
    public PlaybackTimelineSnapshot? Timeline { get; } = timeline;
}

public sealed class MediaSourceChangedEventArgs(
    MediaSourceIdentity? source
) : EventArgs
{
    public MediaSourceIdentity? Source { get; } = source;
}
