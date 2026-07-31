using System.IO;
using System.Windows.Threading;
using Hookline.Audio;
using NAudio.Wave;

namespace Hookline.App;

public sealed class AudioPreviewPlayer : IAudioPreviewPlayer
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private WaveOutEvent? _output;
    private RawSourceWaveStream? _source;
    private MemoryStream? _audioStream;
    private AudioBufferSnapshot? _snapshot;
    private long _playbackStartOffset;
    private bool _disposed;

    public AudioPreviewPlayer(Dispatcher dispatcher)
    {
        _dispatcher =
            dispatcher
            ?? throw new ArgumentNullException(nameof(dispatcher));
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33),
            DispatcherPriority.Render,
            OnTimerTick,
            dispatcher
        );
        _timer.Stop();
    }

    public event EventHandler<AudioPreviewPositionChangedEventArgs>?
        PositionChanged;

    public event EventHandler? PlaybackStopped;

    public bool IsPlaying => _output?.PlaybackState
        == NAudio.Wave.PlaybackState.Playing;

    public TimeSpan CurrentAudioPosition =>
        GetCurrentAudioPosition();

    public void Play(
        AudioBufferSnapshot snapshot,
        TimeSpan resumeAt = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Audio.IsEmpty)
        {
            throw new ArgumentException(
                AppStrings.SelectionHasNoAudio,
                nameof(snapshot)
            );
        }

        StopCore(raiseStopped: false);
        try
        {
            _snapshot = snapshot;
            _audioStream = new MemoryStream(
                snapshot.Audio.ToArray(),
                writable: false
            );
            _source = new RawSourceWaveStream(
                _audioStream,
                new WaveFormat(
                    snapshot.Format.SampleRate,
                    snapshot.Format.BitsPerSample,
                    snapshot.Format.Channels
                )
            );
            _output = new WaveOutEvent
            {
                DesiredLatency = 80,
                NumberOfBuffers = 3,
            };
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Init(_source);
            var playbackOffset = GetPlaybackOffset(
                snapshot,
                resumeAt
            );
            _playbackStartOffset = playbackOffset;
            _source.Position = playbackOffset;
            _output.Play();
            _timer.Start();
            RaisePosition(
                snapshot.Format.GetDuration(playbackOffset)
            );
        }
        catch
        {
            StopCore(raiseStopped: false);
            throw;
        }
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopCore(raiseStopped: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopCore(raiseStopped: false);
        _disposed = true;
    }

    private void OnTimerTick(object? sender, EventArgs args)
    {
        if (_output is null || _snapshot is null)
        {
            return;
        }

        RaisePosition(GetCurrentAudioPosition());
    }

    private void OnPlaybackStopped(
        object? sender,
        StoppedEventArgs args
    )
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(
                () => OnPlaybackStopped(sender, args)
            );
            return;
        }

        if (!ReferenceEquals(sender, _output))
        {
            return;
        }

        var snapshot = _snapshot;
        if (snapshot is not null && args.Exception is null)
        {
            RaisePosition(snapshot.Duration);
        }

        StopCore(raiseStopped: true, stopOutput: false);
    }

    private void RaisePosition(TimeSpan audioPosition)
    {
        if (_snapshot is null)
        {
            return;
        }

        var clamped = audioPosition > _snapshot.Duration
            ? _snapshot.Duration
            : audioPosition;
        var timelinePosition =
            AudioSnapshotSlicer.MapAudioOffsetToTimeline(
                _snapshot,
                clamped
            );
        PositionChanged?.Invoke(
            this,
            new AudioPreviewPositionChangedEventArgs(
                timelinePosition
            )
        );
    }

    private TimeSpan GetCurrentAudioPosition()
    {
        if (_output is null || _snapshot is null)
        {
            return TimeSpan.Zero;
        }

        var audioPosition = _snapshot.Format.GetDuration(
            _playbackStartOffset + _output.GetPosition()
        );
        return audioPosition > _snapshot.Duration
            ? _snapshot.Duration
            : audioPosition;
    }

    private static long GetPlaybackOffset(
        AudioBufferSnapshot snapshot,
        TimeSpan resumeAt
    )
    {
        if (resumeAt <= TimeSpan.Zero)
        {
            return 0;
        }

        var clamped = resumeAt < snapshot.Duration
            ? resumeAt
            : snapshot.Duration;
        var offset = snapshot.Format.GetAlignedByteCount(clamped);
        var lastFrameOffset = Math.Max(
            0,
            snapshot.Audio.Length - snapshot.Format.BlockAlign
        );
        return Math.Min(offset, lastFrameOffset);
    }

    private void StopCore(
        bool raiseStopped,
        bool stopOutput = true
    )
    {
        _timer.Stop();
        var output = _output;
        _output = null;
        if (output is not null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;
            if (
                stopOutput
                && output.PlaybackState
                    != NAudio.Wave.PlaybackState.Stopped
            )
            {
                output.Stop();
            }

            output.Dispose();
        }

        _source?.Dispose();
        _source = null;
        _audioStream?.Dispose();
        _audioStream = null;
        _snapshot = null;
        _playbackStartOffset = 0;

        if (raiseStopped)
        {
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }
    }
}
