using System.Windows.Threading;
using NAudio.Wave;

namespace Hookline.App.Catalog;

public sealed class CatalogAudioPlayer : IClipPlaybackPlayer
{
    private readonly Dispatcher _dispatcher;
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private Guid? _currentClipId;
    private bool _disposed;

    public CatalogAudioPlayer(Dispatcher dispatcher)
    {
        _dispatcher =
            dispatcher
            ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public event EventHandler<ClipPlaybackChangedEventArgs>?
        PlaybackChanged;

    public Guid? CurrentClipId => _currentClipId;

    public bool IsPlaying => _output?.PlaybackState
        == PlaybackState.Playing;

    public void Play(Guid clipId, string filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        StopCore(raiseChanged: false);
        try
        {
            _reader = new AudioFileReader(filePath);
            _output = new WaveOutEvent
            {
                DesiredLatency = 100,
                NumberOfBuffers = 3,
            };
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Init(_reader);
            _currentClipId = clipId;
            _output.Play();
            PlaybackChanged?.Invoke(
                this,
                new ClipPlaybackChangedEventArgs(
                    clipId,
                    isPlaying: true
                )
            );
        }
        catch
        {
            StopCore(raiseChanged: false);
            throw;
        }
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopCore(raiseChanged: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopCore(raiseChanged: false);
        _disposed = true;
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

        var clipId = _currentClipId;
        StopCore(
            raiseChanged: true,
            stopOutput: false,
            error: args.Exception,
            completedClipId: clipId
        );
    }

    private void StopCore(
        bool raiseChanged,
        bool stopOutput = true,
        Exception? error = null,
        Guid? completedClipId = null
    )
    {
        var clipId = completedClipId ?? _currentClipId;
        var output = _output;
        _output = null;
        _currentClipId = null;
        if (output is not null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;
            if (
                stopOutput
                && output.PlaybackState != PlaybackState.Stopped
            )
            {
                output.Stop();
            }

            output.Dispose();
        }

        _reader?.Dispose();
        _reader = null;

        if (raiseChanged)
        {
            PlaybackChanged?.Invoke(
                this,
                new ClipPlaybackChangedEventArgs(
                    clipId,
                    isPlaying: false,
                    error
                )
            );
        }
    }
}
