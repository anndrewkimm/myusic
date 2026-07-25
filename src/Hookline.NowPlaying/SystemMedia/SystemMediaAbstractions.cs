namespace Hookline.NowPlaying.SystemMedia;

internal interface ISystemMediaSessionManagerFactory
{
    ValueTask<ISystemMediaSessionManager> CreateAsync(CancellationToken cancellationToken);
}

internal interface ISystemMediaSessionManager : IDisposable
{
    event EventHandler? SessionsChanged;

    IReadOnlyList<ISystemMediaSession> GetSessions();
}

internal interface ISystemMediaSession
{
    event EventHandler? MediaPropertiesChanged;

    event EventHandler? PlaybackInfoChanged;

    event EventHandler? TimelinePropertiesChanged;

    string SourceAppUserModelId { get; }

    ValueTask<SystemMediaProperties> GetMediaPropertiesAsync(
        CancellationToken cancellationToken
    );

    SystemPlaybackStatus GetPlaybackStatus();

    SystemTimelineProperties GetTimelineProperties();
}

internal sealed record SystemMediaProperties(
    string Title,
    string Artist,
    string Album,
    ReadOnlyMemory<byte> AlbumArt
);

internal sealed record SystemTimelineProperties(
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan Position,
    DateTimeOffset LastUpdatedTime
)
{
    public TimeSpan? Duration =>
        EndTime > StartTime ? EndTime - StartTime : null;
}

internal enum SystemPlaybackStatus
{
    Unavailable,
    Stopped,
    Playing,
    Paused,
}
