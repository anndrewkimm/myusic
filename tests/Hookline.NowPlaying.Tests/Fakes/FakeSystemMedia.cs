using Hookline.NowPlaying.SystemMedia;

namespace Hookline.NowPlaying.Tests.Fakes;

internal sealed class FakeSystemMediaSessionManagerFactory(
    FakeSystemMediaSessionManager manager
) : ISystemMediaSessionManagerFactory
{
    public ValueTask<ISystemMediaSessionManager> CreateAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ISystemMediaSessionManager>(manager);
    }
}

internal sealed class FakeSystemMediaSessionManager
    : ISystemMediaSessionManager
{
    private IReadOnlyList<ISystemMediaSession> _sessions = [];

    public event EventHandler? SessionsChanged;

    public bool IsDisposed { get; private set; }

    public IReadOnlyList<ISystemMediaSession> GetSessions() => _sessions;

    public void SetSessions(params ISystemMediaSession[] sessions)
    {
        _sessions = sessions;
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => IsDisposed = true;
}

internal sealed class FakeSystemMediaSession(string sourceAppUserModelId)
    : ISystemMediaSession
{
    public event EventHandler? MediaPropertiesChanged;

    public event EventHandler? PlaybackInfoChanged;

    public event EventHandler? TimelinePropertiesChanged;

    public string SourceAppUserModelId { get; } = sourceAppUserModelId;

    public SystemMediaProperties MediaProperties { get; set; } =
        new(string.Empty, string.Empty, string.Empty, ReadOnlyMemory<byte>.Empty);

    public SystemPlaybackStatus PlaybackStatus { get; set; } =
        SystemPlaybackStatus.Stopped;

    public SystemTimelineProperties TimelineProperties { get; set; } =
        new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, DateTimeOffset.UnixEpoch);

    public ValueTask<SystemMediaProperties> GetMediaPropertiesAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(MediaProperties);
    }

    public SystemPlaybackStatus GetPlaybackStatus() => PlaybackStatus;

    public SystemTimelineProperties GetTimelineProperties() => TimelineProperties;

    public void RaiseMediaPropertiesChanged() =>
        MediaPropertiesChanged?.Invoke(this, EventArgs.Empty);

    public void RaisePlaybackInfoChanged() =>
        PlaybackInfoChanged?.Invoke(this, EventArgs.Empty);

    public void RaiseTimelinePropertiesChanged() =>
        TimelinePropertiesChanged?.Invoke(this, EventArgs.Empty);
}

internal static class TestWait
{
    public static async Task UntilAsync(
        Func<bool> condition,
        TimeSpan? timeout = null
    )
    {
        var deadline = DateTime.UtcNow
            + (timeout ?? TimeSpan.FromSeconds(2));

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The test condition was not met.");
            }

            await Task.Delay(10);
        }
    }
}
