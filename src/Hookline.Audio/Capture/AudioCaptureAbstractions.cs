namespace Hookline.Audio.Capture;

public enum AudioCaptureMode
{
    ProcessLoopback,
    SystemLoopback,
}

public sealed class AudioDataAvailableEventArgs(
    ReadOnlyMemory<byte> audio,
    DateTimeOffset capturedAt
) : EventArgs
{
    public ReadOnlyMemory<byte> Audio { get; } = audio;

    public DateTimeOffset CapturedAt { get; } = capturedAt;
}

public sealed class AudioBackendStoppedEventArgs(
    Exception? exception = null
) : EventArgs
{
    public Exception? Exception { get; } = exception;
}

public interface IAudioCaptureBackend : IAsyncDisposable
{
    event EventHandler<AudioDataAvailableEventArgs>? DataAvailable;

    event EventHandler<AudioBackendStoppedEventArgs>? Stopped;

    AudioCaptureMode Mode { get; }

    PcmAudioFormat Format { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed record AudioCaptureBackendSelection
{
    public required IAudioCaptureBackend Backend { get; init; }

    public string? FallbackReason { get; init; }
}

public interface IAudioCaptureBackendFactory
{
    Task<AudioCaptureBackendSelection> CreateAsync(
        int? targetProcessId,
        CancellationToken cancellationToken = default
    );
}
