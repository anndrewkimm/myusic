using Hookline.Audio.Capture;
using Hookline.NowPlaying;

namespace Hookline.Audio;

public sealed class HooklineAudioCaptureService : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly INowPlayingWatcher _watcher;
    private readonly IAudioCaptureBackendFactory _backendFactory;
    private readonly AudioCaptureOptions _options;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _backendLock = new(1, 1);

    private CancellationTokenSource? _lifetimeCancellation;
    private IAudioCaptureBackend? _backend;
    private Task? _stallMonitorTask;
    private DateTimeOffset _lastPacketAt;
    private int? _activeTargetProcessId;
    private long? _skipNextPacketForTrack;
    private AudioCaptureStatus _status =
        AudioCaptureStatus.Stopped;
    private string? _statusDetail;
    private string? _fallbackDetail;
    private bool _started;
    private bool _disposed;

    public HooklineAudioCaptureService(
        INowPlayingWatcher watcher,
        IAudioCaptureBackendFactory? backendFactory = null,
        AudioCaptureOptions? options = null,
        PcmAudioFormat? format = null
    )
    {
        _watcher =
            watcher
            ?? throw new ArgumentNullException(nameof(watcher));
        _options = options ?? new AudioCaptureOptions();
        _options.Validate();
        var captureFormat =
            format ?? new PcmAudioFormat(44_100, 16, 2);
        _backendFactory =
            backendFactory
            ?? new DefaultAudioCaptureBackendFactory(
                captureFormat
            );
        Buffer = new RollingAudioBuffer(
            captureFormat,
            _options.BufferWindow
        );
    }

    public event EventHandler<AudioCaptureStatusChangedEventArgs>?
        StatusChanged;

    public RollingAudioBuffer Buffer { get; }

    public AudioCaptureStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public AudioCaptureMode? CaptureMode
    {
        get
        {
            lock (_gate)
            {
                return _backend?.Mode;
            }
        }
    }

    public async Task StartAsync(
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_started)
                {
                    return;
                }

                _started = true;
                _lifetimeCancellation =
                    new CancellationTokenSource();
                _lastPacketAt = DateTimeOffset.UtcNow;
            }

            SubscribeToWatcher();
            PublishStatus(AudioCaptureStatus.Starting);

            try
            {
                await _watcher
                    .StartAsync(cancellationToken)
                    .ConfigureAwait(false);
                await RestartBackendAsync(
                        force: false,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                var lifetimeToken = GetLifetimeToken();
                _stallMonitorTask = MonitorForStallsAsync(
                    lifetimeToken
                );
            }
            catch
            {
                await StopCoreAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default
    )
    {
        await _lifecycleLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await StopCoreAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public AudioBufferSnapshot Query(
        long trackInstanceId,
        TimeSpan? start = null,
        TimeSpan? end = null
    ) => Buffer.Query(trackInstanceId, start, end);

    public AudioBufferSnapshot QueryCurrentTrack(
        TimeSpan? start = null,
        TimeSpan? end = null
    )
    {
        var track =
            _watcher.CurrentTrack
            ?? throw new InvalidOperationException(
                AudioStrings.NoCurrentTrack
            );
        return Query(track.InstanceId, start, end);
    }

    public AudioBufferSnapshot QueryRecentCurrentTrack(
        TimeSpan duration
    )
    {
        var track =
            _watcher.CurrentTrack
            ?? throw new InvalidOperationException(
                AudioStrings.NoCurrentTrack
            );
        return Buffer.QueryRecent(track.InstanceId, duration);
    }

    public async Task DumpRecentCurrentTrackAsync(
        string outputPath,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    )
    {
        var snapshot = QueryRecentCurrentTrack(duration);
        if (snapshot.Audio.IsEmpty)
        {
            throw new InvalidOperationException(
                AudioStrings.NoBufferedAudio
            );
        }

        await WavFileExporter
            .WriteAsync(outputPath, snapshot, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _lifecycleLock.Dispose();
        _backendLock.Dispose();
        _disposed = true;
    }

    private async Task StopCoreAsync(
        CancellationToken cancellationToken
    )
    {
        CancellationTokenSource? lifetimeCancellation;
        Task? monitorTask;
        lock (_gate)
        {
            if (!_started)
            {
                PublishStatus(AudioCaptureStatus.Stopped);
                return;
            }

            _started = false;
            lifetimeCancellation = _lifetimeCancellation;
            _lifetimeCancellation = null;
            monitorTask = _stallMonitorTask;
            _stallMonitorTask = null;
        }

        UnsubscribeFromWatcher();
        lifetimeCancellation?.Cancel();
        if (monitorTask is not null)
        {
            try
            {
                await monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _backendLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await DisposeBackendAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _backendLock.Release();
        }

        await _watcher
            .StopAsync(cancellationToken)
            .ConfigureAwait(false);
        lifetimeCancellation?.Dispose();
        PublishStatus(AudioCaptureStatus.Stopped);
    }

    private void SubscribeToWatcher()
    {
        _watcher.TrackChanged += OnTrackChanged;
        _watcher.PlaybackStateChanged += OnPlaybackStateChanged;
        _watcher.SourceChanged += OnSourceChanged;
    }

    private void UnsubscribeFromWatcher()
    {
        _watcher.TrackChanged -= OnTrackChanged;
        _watcher.PlaybackStateChanged -= OnPlaybackStateChanged;
        _watcher.SourceChanged -= OnSourceChanged;
    }

    private void OnTrackChanged(
        object? sender,
        TrackChangedEventArgs args
    )
    {
        lock (_gate)
        {
            _skipNextPacketForTrack = args.Track.InstanceId;
        }
    }

    private void OnPlaybackStateChanged(
        object? sender,
        PlaybackStateChangedEventArgs args
    )
    {
        if (args.CurrentState == PlaybackState.Playing)
        {
            lock (_gate)
            {
                _lastPacketAt = DateTimeOffset.UtcNow;
            }
        }
    }

    private void OnSourceChanged(
        object? sender,
        MediaSourceChangedEventArgs args
    )
    {
        _ = RunGuardedAsync(
            () =>
                RestartBackendAsync(
                    force: false,
                    GetLifetimeToken()
                )
        );
    }

    private async Task RestartBackendAsync(
        bool force,
        CancellationToken cancellationToken
    )
    {
        await _backendLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            int? targetProcessId;
            lock (_gate)
            {
                if (!_started)
                {
                    return;
                }

                targetProcessId =
                    _watcher.CurrentSource?.ProcessId;
                if (
                    !force
                    && _backend is not null
                    && _activeTargetProcessId == targetProcessId
                )
                {
                    return;
                }
            }

            await DisposeBackendAsync(cancellationToken)
                .ConfigureAwait(false);

            AudioCaptureBackendSelection selection;
            IAudioCaptureBackend? candidate = null;
            try
            {
                selection = await _backendFactory
                    .CreateAsync(
                        targetProcessId,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                candidate = selection.Backend;
                candidate.DataAvailable += OnDataAvailable;
                candidate.Stopped += OnBackendStopped;
                await candidate
                    .StartAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
                when (
                    exception is not OperationCanceledException
                    || !cancellationToken.IsCancellationRequested
                )
            {
                if (candidate is not null)
                {
                    candidate.DataAvailable -= OnDataAvailable;
                    candidate.Stopped -= OnBackendStopped;
                    try
                    {
                        await candidate
                            .StopAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    try
                    {
                        await candidate
                            .DisposeAsync()
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                PublishStatus(
                    AudioCaptureStatus.Failed,
                    null,
                    exception.Message
                );
                return;
            }

            lock (_gate)
            {
                _backend = selection.Backend;
                _activeTargetProcessId = targetProcessId;
                _lastPacketAt = DateTimeOffset.UtcNow;
                _fallbackDetail =
                    selection.Backend.Mode
                    == AudioCaptureMode.SystemLoopback
                        ? selection.FallbackReason
                        : null;
            }

            if (
                selection.Backend.Mode
                == AudioCaptureMode.SystemLoopback
            )
            {
                PublishStatus(
                    AudioCaptureStatus.FallbackLoopback,
                    selection.Backend.Mode,
                    selection.FallbackReason
                );
            }
            else
            {
                PublishStatus(
                    AudioCaptureStatus.Running,
                    selection.Backend.Mode
                );
            }
        }
        finally
        {
            _backendLock.Release();
        }
    }

    private async Task DisposeBackendAsync(
        CancellationToken cancellationToken
    )
    {
        IAudioCaptureBackend? backend;
        lock (_gate)
        {
            backend = _backend;
            _backend = null;
            _activeTargetProcessId = null;
            _fallbackDetail = null;
        }

        if (backend is null)
        {
            return;
        }

        backend.DataAvailable -= OnDataAvailable;
        backend.Stopped -= OnBackendStopped;
        await backend
            .StopAsync(cancellationToken)
            .ConfigureAwait(false);
        await backend.DisposeAsync().ConfigureAwait(false);
    }

    private void OnDataAvailable(
        object? sender,
        AudioDataAvailableEventArgs args
    )
    {
        NowPlayingTrack? track;
        PlaybackTimelineSnapshot? timeline;
        PlaybackState playbackState;
        bool skipPacket;
        AudioCaptureStatus status;
        AudioCaptureMode? mode;
        lock (_gate)
        {
            if (!_started || !ReferenceEquals(sender, _backend))
            {
                return;
            }

            _lastPacketAt = args.CapturedAt;
            track = _watcher.CurrentTrack;
            timeline = _watcher.CurrentTimeline;
            playbackState = _watcher.PlaybackState;
            skipPacket =
                track is not null
                && _skipNextPacketForTrack == track.InstanceId;
            if (skipPacket)
            {
                _skipNextPacketForTrack = null;
            }

            status = _status;
            mode = _backend?.Mode;
        }

        if (status == AudioCaptureStatus.Stalled)
        {
            PublishHealthyStatus(mode);
        }

        if (
            skipPacket
            || track is null
            || timeline is null
            || track.IsLikelyAd
            || playbackState != PlaybackState.Playing
            || args.Audio.IsEmpty
        )
        {
            return;
        }

        var duration = Buffer.Format.GetDuration(args.Audio.Length);
        var playbackEnd = timeline.EstimatePositionAt(
            args.CapturedAt,
            playbackState
        );
        var playbackStart = playbackEnd - duration;
        var audio = args.Audio.Span;
        if (playbackStart < TimeSpan.Zero)
        {
            var bytesToSkip = Buffer.Format.GetAlignedByteCount(
                -playbackStart
            );
            bytesToSkip = Math.Min(bytesToSkip, audio.Length);
            audio = audio[bytesToSkip..];
            playbackStart = TimeSpan.Zero;
        }

        Buffer.Append(
            track.InstanceId,
            playbackStart,
            audio
        );
    }

    private void OnBackendStopped(
        object? sender,
        AudioBackendStoppedEventArgs args
    )
    {
        lock (_gate)
        {
            if (!_started || !ReferenceEquals(sender, _backend))
            {
                return;
            }
        }

        PublishStatus(
            AudioCaptureStatus.Stalled,
            CaptureMode,
            args.Exception?.Message
                ?? AudioStrings.CaptureStoppedUnexpectedly
        );
        _ = RunGuardedAsync(
            () =>
                RestartBackendAsync(
                    force: true,
                    GetLifetimeToken()
                )
        );
    }

    private async Task MonitorForStallsAsync(
        CancellationToken cancellationToken
    )
    {
        using var timer = new PeriodicTimer(
            _options.StallCheckInterval
        );
        while (
            await timer
                .WaitForNextTickAsync(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            DateTimeOffset lastPacketAt;
            AudioCaptureStatus status;
            lock (_gate)
            {
                lastPacketAt = _lastPacketAt;
                status = _status;
            }

            var currentTrack = _watcher.CurrentTrack;
            if (
                currentTrack is null
                || currentTrack.IsLikelyAd
                || _watcher.PlaybackState
                    != PlaybackState.Playing
            )
            {
                continue;
            }

            if (
                status
                    is AudioCaptureStatus.Running
                        or AudioCaptureStatus.FallbackLoopback
                && DateTimeOffset.UtcNow - lastPacketAt
                    >= _options.StallTimeout
            )
            {
                PublishStatus(
                    AudioCaptureStatus.Stalled,
                    CaptureMode,
                    AudioStrings.CaptureStalled
                );
            }
        }
    }

    private void PublishHealthyStatus(AudioCaptureMode? mode)
    {
        if (mode == AudioCaptureMode.SystemLoopback)
        {
            string? fallbackDetail;
            lock (_gate)
            {
                fallbackDetail = _fallbackDetail;
            }

            PublishStatus(
                AudioCaptureStatus.FallbackLoopback,
                mode,
                fallbackDetail
            );
        }
        else
        {
            PublishStatus(AudioCaptureStatus.Running, mode);
        }
    }

    private void PublishStatus(
        AudioCaptureStatus status,
        AudioCaptureMode? mode = null,
        string? detail = null
    )
    {
        lock (_gate)
        {
            if (_status == status && _statusDetail == detail)
            {
                return;
            }

            _status = status;
            _statusDetail = detail;
        }

        RaiseSafely(
            StatusChanged,
            new AudioCaptureStatusChangedEventArgs(
                status,
                mode,
                detail
            )
        );
    }

    private CancellationToken GetLifetimeToken()
    {
        lock (_gate)
        {
            return _lifetimeCancellation?.Token
                ?? new CancellationToken(canceled: true);
        }
    }

    private static async Task RunGuardedAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Status transitions inside the operation surface the failure.
        }
    }

    private void RaiseSafely<TEventArgs>(
        EventHandler<TEventArgs>? handlers,
        TEventArgs args
    )
        where TEventArgs : EventArgs
    {
        if (handlers is null)
        {
            return;
        }

        foreach (
            EventHandler<TEventArgs> handler
                in handlers.GetInvocationList()
        )
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // Consumers must not crash the capture thread.
            }
        }
    }
}
