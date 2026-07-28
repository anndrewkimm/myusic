using Hookline.Audio;
using Hookline.NowPlaying;

namespace Hookline.App;

public sealed record TrimSession
{
    public required NowPlayingTrack Track { get; init; }

    public required AudioBufferSnapshot Snapshot { get; init; }

    public TimeSpan? InitialSelectionStart { get; init; }

    public TimeSpan? InitialSelectionEnd { get; init; }
}
