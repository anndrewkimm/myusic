using Hookline.Audio;

namespace Hookline.App;

public interface IAudioPreviewPlayer : IDisposable
{
    event EventHandler<AudioPreviewPositionChangedEventArgs>?
        PositionChanged;

    event EventHandler? PlaybackStopped;

    bool IsPlaying { get; }

    void Play(AudioBufferSnapshot snapshot);

    void Stop();
}
