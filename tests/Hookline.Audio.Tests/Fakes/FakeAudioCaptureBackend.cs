using Hookline.Audio.Capture;

namespace Hookline.Audio.Tests.Fakes;

internal sealed class FakeAudioCaptureBackend(
    PcmAudioFormat format,
    AudioCaptureMode mode
) : IAudioCaptureBackend
{
    public event EventHandler<AudioDataAvailableEventArgs>? DataAvailable;

    public event EventHandler<AudioBackendStoppedEventArgs>? Stopped;

    public AudioCaptureMode Mode { get; } = mode;

    public PcmAudioFormat Format { get; } = format;

    public bool IsStarted { get; private set; }

    public Task StartAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsStarted = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsStarted = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsStarted = false;
        return ValueTask.CompletedTask;
    }

    public void Emit(byte value, int byteCount)
    {
        DataAvailable?.Invoke(
            this,
            new AudioDataAvailableEventArgs(
                Enumerable.Repeat(value, byteCount).ToArray(),
                DateTimeOffset.UtcNow
            )
        );
    }

    public void Fail(Exception? exception = null) =>
        Stopped?.Invoke(
            this,
            new AudioBackendStoppedEventArgs(exception)
        );
}

internal sealed class FakeAudioCaptureBackendFactory(
    PcmAudioFormat format,
    AudioCaptureMode mode = AudioCaptureMode.ProcessLoopback,
    string? fallbackReason = null
) : IAudioCaptureBackendFactory
{
    private readonly List<FakeAudioCaptureBackend> _backends = [];

    public IReadOnlyList<FakeAudioCaptureBackend> Backends => _backends;

    public FakeAudioCaptureBackend Current => _backends[^1];

    public Task<AudioCaptureBackendSelection> CreateAsync(
        int? targetProcessId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backend = new FakeAudioCaptureBackend(format, mode);
        _backends.Add(backend);
        return Task.FromResult(
            new AudioCaptureBackendSelection
            {
                Backend = backend,
                FallbackReason = fallbackReason,
            }
        );
    }
}
