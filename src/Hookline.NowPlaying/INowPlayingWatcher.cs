namespace Hookline.NowPlaying;

/// <summary>
/// Observes one system media source and reports track and playback-state changes.
/// </summary>
public interface INowPlayingWatcher : IAsyncDisposable
{
    event EventHandler<TrackChangedEventArgs>? TrackChanged;

    event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;

    event EventHandler<PlaybackTimelineChangedEventArgs>?
        PlaybackTimelineChanged;

    event EventHandler<MediaSourceChangedEventArgs>? SourceChanged;

    event EventHandler<WatcherStatusChangedEventArgs>? StatusChanged;

    NowPlayingTrack? CurrentTrack { get; }

    PlaybackState PlaybackState { get; }

    PlaybackTimelineSnapshot? CurrentTimeline { get; }

    MediaSourceIdentity? CurrentSource { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
