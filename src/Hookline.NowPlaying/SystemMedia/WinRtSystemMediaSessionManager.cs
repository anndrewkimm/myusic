using Windows.Media.Control;

namespace Hookline.NowPlaying.SystemMedia;

internal sealed class WinRtSystemMediaSessionManagerFactory
    : ISystemMediaSessionManagerFactory
{
    public async ValueTask<ISystemMediaSessionManager> CreateAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manager = await GlobalSystemMediaTransportControlsSessionManager
            .RequestAsync()
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        return new WinRtSystemMediaSessionManager(manager);
    }
}

internal sealed class WinRtSystemMediaSessionManager
    : ISystemMediaSessionManager
{
    private readonly object _gate = new();
    private readonly GlobalSystemMediaTransportControlsSessionManager _manager;
    private readonly Dictionary<
        GlobalSystemMediaTransportControlsSession,
        WinRtSystemMediaSession
    > _sessionAdapters = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public WinRtSystemMediaSessionManager(
        GlobalSystemMediaTransportControlsSessionManager manager
    )
    {
        _manager = manager;
        _manager.SessionsChanged += OnSessionsChanged;
    }

    public event EventHandler? SessionsChanged;

    public IReadOnlyList<ISystemMediaSession> GetSessions()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var liveSessions = _manager.GetSessions();
        var liveSet = new HashSet<GlobalSystemMediaTransportControlsSession>(
            liveSessions,
            ReferenceEqualityComparer.Instance
        );
        var result = new List<ISystemMediaSession>(liveSessions.Count);

        lock (_gate)
        {
            foreach (var session in liveSessions)
            {
                if (!_sessionAdapters.TryGetValue(session, out var adapter))
                {
                    adapter = new WinRtSystemMediaSession(session);
                    _sessionAdapters.Add(session, adapter);
                }

                result.Add(adapter);
            }

            foreach (
                var removedSession in _sessionAdapters.Keys
                    .Where(session => !liveSet.Contains(session))
                    .ToArray()
            )
            {
                _sessionAdapters.Remove(removedSession, out var adapter);
                adapter?.Dispose();
            }
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manager.SessionsChanged -= OnSessionsChanged;

        lock (_gate)
        {
            foreach (var adapter in _sessionAdapters.Values)
            {
                adapter.Dispose();
            }

            _sessionAdapters.Clear();
        }
    }

    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args
    ) => SessionsChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class WinRtSystemMediaSession : ISystemMediaSession, IDisposable
{
    private readonly GlobalSystemMediaTransportControlsSession _session;
    private bool _disposed;

    public WinRtSystemMediaSession(
        GlobalSystemMediaTransportControlsSession session
    )
    {
        _session = session;
        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
    }

    public event EventHandler? MediaPropertiesChanged;

    public event EventHandler? PlaybackInfoChanged;

    public event EventHandler? TimelinePropertiesChanged;

    public string SourceAppUserModelId => _session.SourceAppUserModelId;

    public async ValueTask<SystemMediaProperties> GetMediaPropertiesAsync(
        CancellationToken cancellationToken
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var properties = await _session
            .TryGetMediaPropertiesAsync()
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        var albumArt = await ReadAlbumArtAsync(
                properties.Thumbnail,
                cancellationToken
            )
            .ConfigureAwait(false);

        return new SystemMediaProperties(
            properties.Title ?? string.Empty,
            properties.Artist ?? string.Empty,
            properties.AlbumTitle ?? string.Empty,
            albumArt
        );
    }

    public SystemPlaybackStatus GetPlaybackStatus()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _session.GetPlaybackInfo().PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing =>
                SystemPlaybackStatus.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused =>
                SystemPlaybackStatus.Paused,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed =>
                SystemPlaybackStatus.Unavailable,
            _ => SystemPlaybackStatus.Stopped,
        };
    }

    public SystemTimelineProperties GetTimelineProperties()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var timeline = _session.GetTimelineProperties();
        return new SystemTimelineProperties(
            timeline.StartTime,
            timeline.EndTime,
            timeline.Position,
            timeline.LastUpdatedTime
        );
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
    }

    private static async Task<ReadOnlyMemory<byte>> ReadAlbumArtAsync(
        Windows.Storage.Streams.IRandomAccessStreamReference? thumbnail,
        CancellationToken cancellationToken
    )
    {
        if (thumbnail is null)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        using var randomAccessStream = await thumbnail
            .OpenReadAsync()
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        using var stream = randomAccessStream.AsStreamForRead();
        using var destination = new MemoryStream();

        await stream
            .CopyToAsync(destination, cancellationToken)
            .ConfigureAwait(false);
        return destination.ToArray();
    }

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args
    ) => MediaPropertiesChanged?.Invoke(this, EventArgs.Empty);

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args
    ) => PlaybackInfoChanged?.Invoke(this, EventArgs.Empty);

    private void OnTimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args
    ) => TimelinePropertiesChanged?.Invoke(this, EventArgs.Empty);
}
