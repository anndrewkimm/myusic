using NAudio.Wave;

namespace Hookline.Audio.Capture;

internal sealed class SystemLoopbackAudioCaptureBackend :
    IAudioCaptureBackend
{
    private readonly object _gate = new();
    private readonly WasapiLoopbackCapture _capture;

    private bool _started;
    private bool _disposed;

    public SystemLoopbackAudioCaptureBackend(PcmAudioFormat format)
    {
        Format = format;
        _capture = new WasapiLoopbackCapture
        {
            WaveFormat = new WaveFormat(
                format.SampleRate,
                format.BitsPerSample,
                format.Channels
            ),
        };
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    public event EventHandler<AudioDataAvailableEventArgs>? DataAvailable;

    public event EventHandler<AudioBackendStoppedEventArgs>? Stopped;

    public AudioCaptureMode Mode => AudioCaptureMode.SystemLoopback;

    public PcmAudioFormat Format { get; }

    public Task StartAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_started)
            {
                return Task.CompletedTask;
            }

            _capture.StartRecording();
            _started = true;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_started)
            {
                return Task.CompletedTask;
            }

            _started = false;
            _capture.StopRecording();
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose();
        _disposed = true;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        var copy = args.Buffer
            .AsSpan(0, args.BytesRecorded)
            .ToArray();
        DataAvailable?.Invoke(
            this,
            new AudioDataAvailableEventArgs(
                copy,
                DateTimeOffset.UtcNow
            )
        );
    }

    private void OnRecordingStopped(
        object? sender,
        StoppedEventArgs args
    )
    {
        lock (_gate)
        {
            _started = false;
        }

        Stopped?.Invoke(
            this,
            new AudioBackendStoppedEventArgs(args.Exception)
        );
    }
}
