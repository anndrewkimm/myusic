namespace Hookline.App.Catalog;

public interface IClipPlaybackPlayer : IDisposable
{
    event EventHandler<ClipPlaybackChangedEventArgs>? PlaybackChanged;

    Guid? CurrentClipId { get; }

    bool IsPlaying { get; }

    void Play(Guid clipId, string filePath);

    void Stop();
}
