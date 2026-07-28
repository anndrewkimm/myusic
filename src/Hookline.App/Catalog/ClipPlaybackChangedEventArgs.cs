namespace Hookline.App.Catalog;

public sealed class ClipPlaybackChangedEventArgs(
    Guid? clipId,
    bool isPlaying,
    Exception? error = null
) : EventArgs
{
    public Guid? ClipId { get; } = clipId;

    public bool IsPlaying { get; } = isPlaying;

    public Exception? Error { get; } = error;
}
