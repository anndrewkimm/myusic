using Hookline.Audio.Capture;

namespace Hookline.Audio;

public enum AudioCaptureStatus
{
    Stopped,
    Starting,
    Running,
    Stalled,
    Failed,
    FallbackLoopback,
}

public sealed class AudioCaptureStatusChangedEventArgs(
    AudioCaptureStatus status,
    AudioCaptureMode? mode = null,
    string? detail = null
) : EventArgs
{
    public AudioCaptureStatus Status { get; } = status;

    public AudioCaptureMode? Mode { get; } = mode;

    public string? Detail { get; } = detail;
}
