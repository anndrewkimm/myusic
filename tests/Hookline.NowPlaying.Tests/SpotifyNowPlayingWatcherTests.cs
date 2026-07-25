using System.Collections.Concurrent;
using Hookline.NowPlaying.SystemMedia;
using Hookline.NowPlaying.Tests.Fakes;

namespace Hookline.NowPlaying.Tests;

public sealed class SpotifyNowPlayingWatcherTests
{
    private static readonly DateTimeOffset TimelineStart =
        DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public async Task FindsSpotifyAfterStartupAndIgnoresOtherSessions()
    {
        var manager = new FakeSystemMediaSessionManager();
        var browser = CreateSession("chrome.exe", "Browser video");
        manager.SetSessions(browser);

        await using var watcher = CreateWatcher(manager);
        var tracks = new ConcurrentQueue<NowPlayingTrack>();
        var statuses = new ConcurrentQueue<NowPlayingWatcherStatus>();
        watcher.TrackChanged += (_, args) => tracks.Enqueue(args.Track);
        watcher.StatusChanged += (_, args) => statuses.Enqueue(args.Status);

        await watcher.StartAsync();

        Assert.Null(watcher.CurrentTrack);
        Assert.Equal(PlaybackState.Unavailable, watcher.PlaybackState);
        Assert.Contains(
            NowPlayingWatcherStatus.WaitingForSource,
            statuses
        );

        var spotify = CreateSession("Spotify.exe", "Night Drive");
        manager.SetSessions(browser, spotify);

        await TestWait.UntilAsync(() => tracks.Count == 1);

        var track = Assert.Single(tracks);
        Assert.Equal("Night Drive", track.Title);
        Assert.Equal("Hookline Artist", track.Artist);
        Assert.Equal("Test Album", track.Album);
        Assert.Equal(TimeSpan.FromMinutes(4), track.Duration);
        Assert.Equal([1, 2, 3], track.AlbumArt.ToArray());
        Assert.Equal(PlaybackState.Playing, watcher.PlaybackState);
        Assert.Contains(NowPlayingWatcherStatus.Monitoring, statuses);

        manager.SetSessions(browser);
        await TestWait.UntilAsync(
            () => watcher.PlaybackState == PlaybackState.Unavailable
        );

        Assert.Null(watcher.CurrentTrack);
        Assert.Single(tracks);
    }

    [Fact]
    public async Task PauseResumeAndSeekDoNotCreateTrackChanges()
    {
        var manager = new FakeSystemMediaSessionManager();
        var spotify = CreateSession("Spotify.exe", "One Song");
        manager.SetSessions(spotify);

        await using var watcher = CreateWatcher(manager);
        var tracks = new ConcurrentQueue<NowPlayingTrack>();
        var playbackStates = new ConcurrentQueue<PlaybackState>();
        watcher.TrackChanged += (_, args) => tracks.Enqueue(args.Track);
        watcher.PlaybackStateChanged += (_, args) =>
            playbackStates.Enqueue(args.CurrentState);

        await watcher.StartAsync();
        Assert.Single(tracks);

        spotify.PlaybackStatus = SystemPlaybackStatus.Paused;
        spotify.RaisePlaybackInfoChanged();
        await TestWait.UntilAsync(
            () => watcher.PlaybackState == PlaybackState.Paused
        );

        spotify.PlaybackStatus = SystemPlaybackStatus.Playing;
        spotify.RaisePlaybackInfoChanged();
        await TestWait.UntilAsync(
            () => watcher.PlaybackState == PlaybackState.Playing
        );

        spotify.TimelineProperties = Timeline(
            positionSeconds: 25,
            updatedAt: TimelineStart.AddSeconds(1)
        );
        spotify.RaiseTimelinePropertiesChanged();
        await Task.Delay(75);

        Assert.Single(tracks);
        Assert.Contains(PlaybackState.Paused, playbackStates);
        Assert.Equal("One Song", watcher.CurrentTrack?.Title);
    }

    [Fact]
    public async Task ReplayOfSameSongGetsNewMonotonicInstanceId()
    {
        var manager = new FakeSystemMediaSessionManager();
        var spotify = CreateSession("Spotify.exe", "Looped Song");
        manager.SetSessions(spotify);

        await using var watcher = CreateWatcher(manager);
        var tracks = new ConcurrentQueue<NowPlayingTrack>();
        watcher.TrackChanged += (_, args) => tracks.Enqueue(args.Track);

        await watcher.StartAsync();
        var firstTrack = Assert.Single(tracks);

        spotify.TimelineProperties = Timeline(
            positionSeconds: 120,
            updatedAt: TimelineStart.AddSeconds(110)
        );
        spotify.RaiseTimelinePropertiesChanged();
        await Task.Delay(75);
        Assert.Single(tracks);

        spotify.TimelineProperties = Timeline(
            positionSeconds: 1,
            updatedAt: TimelineStart.AddSeconds(111)
        );
        spotify.RaiseTimelinePropertiesChanged();

        await TestWait.UntilAsync(() => tracks.Count == 2);

        var emittedTracks = tracks.ToArray();
        Assert.Equal("Looped Song", emittedTracks[1].Title);
        Assert.True(
            emittedTracks[1].InstanceId > firstTrack.InstanceId
        );
    }

    [Fact]
    public async Task RapidTrackChangesOnlyEmitFinalSettledTrack()
    {
        var manager = new FakeSystemMediaSessionManager();
        var spotify = CreateSession("Spotify.exe", "Track A");
        manager.SetSessions(spotify);

        await using var watcher = CreateWatcher(manager);
        var tracks = new ConcurrentQueue<NowPlayingTrack>();
        watcher.TrackChanged += (_, args) => tracks.Enqueue(args.Track);

        await watcher.StartAsync();
        Assert.Single(tracks);

        spotify.MediaProperties = Media("Track B");
        spotify.RaiseMediaPropertiesChanged();
        spotify.MediaProperties = Media("Track C");
        spotify.RaiseMediaPropertiesChanged();

        await TestWait.UntilAsync(() => tracks.Count == 2);
        await Task.Delay(75);

        Assert.Equal(["Track A", "Track C"], tracks.Select(track => track.Title));
        Assert.Equal([1L, 2L], tracks.Select(track => track.InstanceId));
    }

    [Fact]
    public async Task ExplicitAdvertisementTitleIsFlagged()
    {
        var manager = new FakeSystemMediaSessionManager();
        var spotify = CreateSession("Spotify.exe", "Advertisement");
        manager.SetSessions(spotify);

        await using var watcher = CreateWatcher(manager);
        await watcher.StartAsync();

        Assert.True(watcher.CurrentTrack?.IsLikelyAd);
    }

    [Fact]
    public async Task ExposesSelectedSourceProcessAndPlaybackTimeline()
    {
        var manager = new FakeSystemMediaSessionManager();
        var spotify = CreateSession("Spotify.exe", "Timeline Song");
        spotify.TimelineProperties = Timeline(
            positionSeconds: 37,
            updatedAt: TimelineStart
        );
        manager.SetSessions(spotify);

        await using var watcher = new SpotifyNowPlayingWatcher(
            new FakeSystemMediaSessionManagerFactory(manager),
            new FixedSourceProcessResolver(4_242),
            TimeSpan.FromMilliseconds(20)
        );

        await watcher.StartAsync();

        Assert.Equal("Spotify.exe", watcher.CurrentSource?.ApplicationId);
        Assert.Equal(4_242, watcher.CurrentSource?.ProcessId);
        Assert.Equal(
            TimeSpan.FromSeconds(37),
            watcher.CurrentTimeline?.Position
        );
        Assert.Equal(
            TimeSpan.FromSeconds(39),
            watcher.CurrentTimeline?.EstimatePositionAt(
                TimelineStart.AddSeconds(2),
                PlaybackState.Playing
            )
        );
    }

    private static SpotifyNowPlayingWatcher CreateWatcher(
        FakeSystemMediaSessionManager manager
    ) =>
        new(
            new FakeSystemMediaSessionManagerFactory(manager),
            TimeSpan.FromMilliseconds(20)
        );

    private static FakeSystemMediaSession CreateSession(
        string sourceId,
        string title
    ) =>
        new(sourceId)
        {
            MediaProperties = Media(title),
            PlaybackStatus = SystemPlaybackStatus.Playing,
            TimelineProperties = Timeline(
                positionSeconds: 10,
                updatedAt: TimelineStart
            ),
        };

    private static SystemMediaProperties Media(string title) =>
        new(
            title,
            "Hookline Artist",
            "Test Album",
            new byte[] { 1, 2, 3 }
        );

    private static SystemTimelineProperties Timeline(
        int positionSeconds,
        DateTimeOffset updatedAt
    ) =>
        new(
            TimeSpan.Zero,
            TimeSpan.FromMinutes(4),
            TimeSpan.FromSeconds(positionSeconds),
            updatedAt
        );

    private sealed class FixedSourceProcessResolver(int processId) :
        ISourceProcessResolver
    {
        public MediaSourceIdentity Resolve(string applicationId) =>
            new()
            {
                ApplicationId = applicationId,
                ProcessId = processId,
            };
    }
}
