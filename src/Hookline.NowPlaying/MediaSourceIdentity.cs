namespace Hookline.NowPlaying;

/// <summary>
/// Identifies the application that owns the selected media session.
/// </summary>
public sealed record MediaSourceIdentity
{
    public required string ApplicationId { get; init; }

    public int? ProcessId { get; init; }
}
