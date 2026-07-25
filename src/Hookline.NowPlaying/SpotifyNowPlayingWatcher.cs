using Hookline.NowPlaying.SystemMedia;

namespace Hookline.NowPlaying;

public sealed class SpotifyNowPlayingWatcher : INowPlayingWatcher
{
    private static readonly TimeSpan DefaultMediaDebounceDelay =
        TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private readonly ISystemMediaSessionManagerFactory _managerFactory;
    private readonly ISourceProcessResolver _sourceProcessResolver;
    private readonly AsyncDebouncer _mediaDebouncer;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private ISystemMediaSessionManager? _manager;
    private ISystemMediaSession? _session;
    private CancellationTokenSource? _lifetimeCancellation;
    private NowPlayingTrack? _currentTrack;
    private MediaSourceIdentity? _currentSource;
    private PlaybackTimelineSnapshot? _currentTimeline;
    private MediaIdentity? _currentIdentity;
    private SystemTimelineProperties? _previousTimeline;
    private PlaybackState _playbackState = PlaybackState.Unavailable;
    private NowPlayingWatcherStatus _status = NowPlayingWatcherStatus.Stopped;
    private string? _statusDetail;
    private long _nextTrackInstanceId;
    private bool _started;
    private bool _disposed;

    public SpotifyNowPlayingWatcher()
        : this(
            new WinRtSystemMediaSessionManagerFactory(),
            new SpotifySourceProcessResolver(),
            DefaultMediaDebounceDelay
        )
    {
    }

    internal SpotifyNowPlayingWatcher(
        ISystemMediaSessionManagerFactory managerFactory,
        TimeSpan mediaDebounceDelay
    )
        : this(
            managerFactory,
            new EmptySourceProcessResolver(),
            mediaDebounceDelay
        )
    {
    }

    internal SpotifyNowPlayingWatcher(
        ISystemMediaSessionManagerFactory managerFactory,
        ISourceProcessResolver sourceProcessResolver,
        TimeSpan mediaDebounceDelay
    )
    {
        _managerFactory =
            managerFactory
            ?? throw new ArgumentNullException(nameof(managerFactory));
        _sourceProcessResolver =
            sourceProcessResolver
            ?? throw new ArgumentNullException(
                nameof(sourceProcessResolver)
            );
        _mediaDebouncer = new AsyncDebouncer(mediaDebounceDelay);
    }

    public event EventHandler<TrackChangedEventArgs>? TrackChanged;

    public event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;

    public event EventHandler<PlaybackTimelineChangedEventArgs>?
        PlaybackTimelineChanged;

    public event EventHandler<MediaSourceChangedEventArgs>? SourceChanged;

    public event EventHandler<WatcherStatusChangedEventArgs>? StatusChanged;

    public NowPlayingTrack? CurrentTrack
    {
        get
        {
            lock (_gate)
            {
                return _currentTrack;
            }
        }
    }

    public PlaybackState PlaybackState
    {
        get
        {
            lock (_gate)
            {
                return _playbackState;
            }
        }
    }

    public PlaybackTimelineSnapshot? CurrentTimeline
    {
        get
        {
            lock (_gate)
            {
                return _currentTimeline;
            }
        }
    }

    public MediaSourceIdentity? CurrentSource
    {
        get
        {
            lock (_gate)
            {
                return _currentSource;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lifecycleLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_started)
                {
                    return;
                }
            }

            PublishStatus(NowPlayingWatcherStatus.Starting);

            ISystemMediaSessionManager manager;
            try
            {
                manager = await _managerFactory
                    .CreateAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                PublishStatus(
                    NowPlayingWatcherStatus.Error,
                    exception.Message
                );
                throw;
            }

            lock (_gate)
            {
                _manager = manager;
                _lifetimeCancellation = new CancellationTokenSource();
                _started = true;
            }

            manager.SessionsChanged += OnSessionsChanged;

            await RunGuardedAsync(
                    () => ReconcileSessionAsync(cancellationToken),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ISystemMediaSessionManager? manager;
            ISystemMediaSession? session;
            CancellationTokenSource? lifetimeCancellation;
            PlaybackState previousPlaybackState;
            MediaSourceIdentity? previousSource;
            PlaybackTimelineSnapshot? previousTimeline;

            lock (_gate)
            {
                if (!_started)
                {
                    PublishStatus(NowPlayingWatcherStatus.Stopped);
                    return;
                }

                _started = false;
                manager = _manager;
                session = _session;
                lifetimeCancellation = _lifetimeCancellation;
                previousPlaybackState = _playbackState;
                previousSource = _currentSource;
                previousTimeline = _currentTimeline;

                _manager = null;
                _session = null;
                _lifetimeCancellation = null;
                _currentTrack = null;
                _currentSource = null;
                _currentTimeline = null;
                _currentIdentity = null;
                _previousTimeline = null;
                _playbackState = PlaybackState.Unavailable;
            }

            lifetimeCancellation?.Cancel();
            await _mediaDebouncer.CancelAsync().ConfigureAwait(false);

            await _reconcileLock.WaitAsync().ConfigureAwait(false);
            _reconcileLock.Release();
            await _refreshLock.WaitAsync().ConfigureAwait(false);
            _refreshLock.Release();

            if (session is not null)
            {
                UnsubscribeFromSession(session);
            }

            if (manager is not null)
            {
                manager.SessionsChanged -= OnSessionsChanged;
                manager.Dispose();
            }

            lifetimeCancellation?.Dispose();

            if (previousPlaybackState != PlaybackState.Unavailable)
            {
                RaiseSafely(
                    PlaybackStateChanged,
                    new PlaybackStateChangedEventArgs(
                        previousPlaybackState,
                        PlaybackState.Unavailable
                    )
                );
            }

            if (previousTimeline is not null)
            {
                RaiseSafely(
                    PlaybackTimelineChanged,
                    new PlaybackTimelineChangedEventArgs(null)
                );
            }

            if (previousSource is not null)
            {
                RaiseSafely(
                    SourceChanged,
                    new MediaSourceChangedEventArgs(null)
                );
            }

            PublishStatus(NowPlayingWatcherStatus.Stopped);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        await _mediaDebouncer.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private void OnSessionsChanged(object? sender, EventArgs args)
    {
        var lifetimeToken = GetLifetimeToken();
        if (lifetimeToken is null)
        {
            return;
        }

        _ = RunGuardedAsync(
            () => ReconcileSessionAsync(lifetimeToken.Value),
            lifetimeToken.Value
        );
    }

    private void OnMediaPropertiesChanged(object? sender, EventArgs args) =>
        ScheduleSettledMediaRefresh(sender as ISystemMediaSession);

    private void OnTimelinePropertiesChanged(object? sender, EventArgs args) =>
        ScheduleSettledMediaRefresh(sender as ISystemMediaSession);

    private void OnPlaybackInfoChanged(object? sender, EventArgs args)
    {
        if (sender is not ISystemMediaSession session || !IsCurrentSession(session))
        {
            return;
        }

        try
        {
            UpdatePlaybackState(MapPlaybackState(session.GetPlaybackStatus()));
            ScheduleSettledMediaRefresh(session);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    private void ScheduleSettledMediaRefresh(ISystemMediaSession? session)
    {
        var lifetimeToken = GetLifetimeToken();
        if (
            session is null
            || lifetimeToken is null
            || !IsCurrentSession(session)
        )
        {
            return;
        }

        _mediaDebouncer.Trigger(
            token =>
                RunGuardedAsync(
                    () => RefreshMediaAsync(session, token),
                    token
                ),
            lifetimeToken.Value
        );
    }

    private async Task ReconcileSessionAsync(
        CancellationToken cancellationToken
    )
    {
        await _reconcileLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ISystemMediaSessionManager? manager;
            lock (_gate)
            {
                manager = _started ? _manager : null;
            }

            if (manager is null)
            {
                return;
            }

            var selectedSession = SpotifySessionFilter.Select(
                manager.GetSessions()
            );
            ISystemMediaSession? previousSession;
            MediaSourceIdentity? changedSource = null;
            var sourceWasCleared = false;
            var sessionChanged = false;

            lock (_gate)
            {
                if (!_started || !ReferenceEquals(_manager, manager))
                {
                    return;
                }

                previousSession = _session;
                sessionChanged = !ReferenceEquals(
                    previousSession,
                    selectedSession
                );

                if (sessionChanged)
                {
                    _session = selectedSession;
                    _currentTrack = null;
                    _currentIdentity = null;
                    _previousTimeline = null;
                    _currentTimeline = null;

                    var newSource =
                        selectedSession is null
                            ? null
                            : _sourceProcessResolver.Resolve(
                                selectedSession.SourceAppUserModelId
                            );
                    if (_currentSource != newSource)
                    {
                        _currentSource = newSource;
                        changedSource = newSource;
                        sourceWasCleared = newSource is null;
                    }
                }
            }

            if (sessionChanged)
            {
                await _mediaDebouncer.CancelAsync().ConfigureAwait(false);

                RaiseSafely(
                    PlaybackTimelineChanged,
                    new PlaybackTimelineChangedEventArgs(null)
                );

                if (changedSource is not null || sourceWasCleared)
                {
                    RaiseSafely(
                        SourceChanged,
                        new MediaSourceChangedEventArgs(changedSource)
                    );
                }

                if (previousSession is not null)
                {
                    UnsubscribeFromSession(previousSession);
                }

                if (selectedSession is not null)
                {
                    SubscribeToSession(selectedSession);
                }
            }

            if (selectedSession is null)
            {
                UpdatePlaybackState(PlaybackState.Unavailable);
                PublishStatus(NowPlayingWatcherStatus.WaitingForSource);
                return;
            }

            UpdatePlaybackState(
                MapPlaybackState(selectedSession.GetPlaybackStatus())
            );
            PublishStatus(NowPlayingWatcherStatus.Monitoring);
            await RefreshMediaAsync(selectedSession, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _reconcileLock.Release();
        }
    }

    private async Task RefreshMediaAsync(
        ISystemMediaSession session,
        CancellationToken cancellationToken
    )
    {
        await _refreshLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (!IsCurrentSession(session))
            {
                return;
            }

            var properties = await session
                .GetMediaPropertiesAsync(cancellationToken)
                .ConfigureAwait(false);
            var timeline = session.GetTimelineProperties();
            var publicTimeline = new PlaybackTimelineSnapshot
            {
                Position = timeline.Position - timeline.StartTime,
                LastUpdatedTime = timeline.LastUpdatedTime,
            };
            var identity = MediaIdentity.From(properties);

            if (identity.IsEmpty)
            {
                return;
            }

            NowPlayingTrack? changedTrack = null;
            var timelineChanged = false;

            lock (_gate)
            {
                if (!_started || !ReferenceEquals(_session, session))
                {
                    return;
                }

                var identityChanged =
                    _currentIdentity is null
                    || _currentIdentity.Value != identity;
                var replayed =
                    !identityChanged
                    && TrackChangeDetector.IsReplay(
                        _previousTimeline,
                        timeline,
                        timeline.Duration ?? _currentTrack?.Duration,
                        _playbackState
                    );

                _previousTimeline = timeline;
                if (_currentTimeline != publicTimeline)
                {
                    _currentTimeline = publicTimeline;
                    timelineChanged = true;
                }

                if (!identityChanged && !replayed && _currentTrack is not null)
                {
                    _currentTrack = _currentTrack with
                    {
                        Title = identity.Title,
                        Artist = identity.Artist,
                        Album = identity.Album,
                        Duration = timeline.Duration ?? _currentTrack.Duration,
                        AlbumArt = properties.AlbumArt.IsEmpty
                            ? _currentTrack.AlbumArt
                            : properties.AlbumArt,
                        IsLikelyAd = TrackChangeDetector.IsLikelyAd(
                            identity.Title
                        ),
                    };
                }
                else
                {
                    _currentIdentity = identity;
                    changedTrack = new NowPlayingTrack
                    {
                        InstanceId = ++_nextTrackInstanceId,
                        Title = identity.Title,
                        Artist = identity.Artist,
                        Album = identity.Album,
                        Duration = timeline.Duration,
                        AlbumArt = properties.AlbumArt,
                        IsLikelyAd = TrackChangeDetector.IsLikelyAd(
                            identity.Title
                        ),
                    };
                    _currentTrack = changedTrack;
                }
            }

            if (timelineChanged)
            {
                RaiseSafely(
                    PlaybackTimelineChanged,
                    new PlaybackTimelineChangedEventArgs(publicTimeline)
                );
            }

            if (changedTrack is not null)
            {
                RaiseSafely(
                    TrackChanged,
                    new TrackChangedEventArgs(changedTrack)
                );
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsCurrentSession(ISystemMediaSession session)
    {
        lock (_gate)
        {
            return _started && ReferenceEquals(_session, session);
        }
    }

    private CancellationToken? GetLifetimeToken()
    {
        lock (_gate)
        {
            return _started ? _lifetimeCancellation?.Token : null;
        }
    }

    private void SubscribeToSession(ISystemMediaSession session)
    {
        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
    }

    private void UnsubscribeFromSession(ISystemMediaSession session)
    {
        session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
    }

    private void UpdatePlaybackState(PlaybackState newState)
    {
        PlaybackState previousState;

        lock (_gate)
        {
            previousState = _playbackState;
            if (previousState == newState)
            {
                return;
            }

            _playbackState = newState;
        }

        RaiseSafely(
            PlaybackStateChanged,
            new PlaybackStateChangedEventArgs(previousState, newState)
        );
    }

    private void PublishStatus(
        NowPlayingWatcherStatus status,
        string? detail = null
    )
    {
        lock (_gate)
        {
            if (_status == status && _statusDetail == detail)
            {
                return;
            }

            _status = status;
            _statusDetail = detail;
        }

        RaiseSafely(
            StatusChanged,
            new WatcherStatusChangedEventArgs(status, detail)
        );
    }

    private async Task RunGuardedAsync(
        Func<Task> action,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    private void ReportError(Exception exception) =>
        PublishStatus(NowPlayingWatcherStatus.Error, exception.Message);

    private static PlaybackState MapPlaybackState(
        SystemPlaybackStatus playbackStatus
    ) =>
        playbackStatus switch
        {
            SystemPlaybackStatus.Playing => PlaybackState.Playing,
            SystemPlaybackStatus.Paused => PlaybackState.Paused,
            SystemPlaybackStatus.Stopped => PlaybackState.Stopped,
            _ => PlaybackState.Unavailable,
        };

    private void RaiseSafely<TEventArgs>(
        EventHandler<TEventArgs>? handlers,
        TEventArgs args
    )
        where TEventArgs : EventArgs
    {
        if (handlers is null)
        {
            return;
        }

        foreach (
            EventHandler<TEventArgs> handler in handlers.GetInvocationList()
        )
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // A consumer's event handler must not crash the watcher thread.
            }
        }
    }

    private readonly record struct MediaIdentity(
        string Title,
        string Artist,
        string Album
    )
    {
        public bool IsEmpty =>
            Title.Length == 0 && Artist.Length == 0 && Album.Length == 0;

        public static MediaIdentity From(SystemMediaProperties properties) =>
            new(
                properties.Title.Trim(),
                properties.Artist.Trim(),
                properties.Album.Trim()
            );
    }
}
