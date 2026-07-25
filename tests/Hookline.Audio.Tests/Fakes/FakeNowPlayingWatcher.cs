using Hookline.NowPlaying;

namespace Hookline.Audio.Tests.Fakes;

internal sealed class FakeNowPlayingWatcher : INowPlayingWatcher
{
    public event EventHandler<TrackChangedEventArgs>? TrackChanged;

    public event EventHandler<PlaybackStateChangedEventArgs>?
        PlaybackStateChanged;

    public event EventHandler<PlaybackTimelineChangedEventArgs>?
        PlaybackTimelineChanged;

    public event EventHandler<MediaSourceChangedEventArgs>?
        SourceChanged;

    public event EventHandler<WatcherStatusChangedEventArgs>? StatusChanged;

    public NowPlayingTrack? CurrentTrack { get; private set; }

    public PlaybackState PlaybackState { get; private set; } =
        PlaybackState.Playing;

    public PlaybackTimelineSnapshot? CurrentTimeline { get; private set; }

    public MediaSourceIdentity? CurrentSource { get; private set; } =
        new()
        {
            ApplicationId = "Spotify.exe",
            ProcessId = 42,
        };

    public Task StartAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        StatusChanged?.Invoke(
            this,
            new WatcherStatusChangedEventArgs(
                NowPlayingWatcherStatus.Monitoring
            )
        );
        if (CurrentTrack is not null)
        {
            TrackChanged?.Invoke(
                this,
                new TrackChangedEventArgs(CurrentTrack)
            );
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void SetTrack(
        long instanceId,
        bool isLikelyAd = false,
        TimeSpan? position = null
    )
    {
        CurrentTrack = new NowPlayingTrack
        {
            InstanceId = instanceId,
            Title = isLikelyAd ? "Advertisement" : $"Track {instanceId}",
            Artist = "Artist",
            Album = "Album",
            Duration = TimeSpan.FromMinutes(4),
            IsLikelyAd = isLikelyAd,
        };
        CurrentTimeline = new PlaybackTimelineSnapshot
        {
            Position = position ?? TimeSpan.FromSeconds(10),
            LastUpdatedTime = DateTimeOffset.UtcNow,
        };
        TrackChanged?.Invoke(
            this,
            new TrackChangedEventArgs(CurrentTrack)
        );
        PlaybackTimelineChanged?.Invoke(
            this,
            new PlaybackTimelineChangedEventArgs(CurrentTimeline)
        );
    }

    public void SetPlaybackState(PlaybackState state)
    {
        var previous = PlaybackState;
        PlaybackState = state;
        PlaybackStateChanged?.Invoke(
            this,
            new PlaybackStateChangedEventArgs(previous, state)
        );
    }

    public void SetSource(int? processId)
    {
        CurrentSource = new MediaSourceIdentity
        {
            ApplicationId = "Spotify.exe",
            ProcessId = processId,
        };
        SourceChanged?.Invoke(
            this,
            new MediaSourceChangedEventArgs(CurrentSource)
        );
    }
}
