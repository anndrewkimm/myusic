using System.Windows;
using System.Windows.Threading;
using Hookline.Audio;
using Hookline.NowPlaying;

namespace Hookline.App.Catalog;

public sealed class ClipRetrimLauncher : IClipRetrimLauncher
{
    private readonly HooklineAudioCaptureService _captureService;
    private readonly IClipExporter _exporter;
    private readonly OutputFolderSettings _outputSettings;
    private readonly IStemIsolationService _stemIsolationService;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Guid, TrimWindow> _windows = [];

    public ClipRetrimLauncher(
        HooklineAudioCaptureService captureService,
        IClipExporter exporter,
        OutputFolderSettings outputSettings,
        IStemIsolationService stemIsolationService,
        Dispatcher dispatcher
    )
    {
        _captureService =
            captureService
            ?? throw new ArgumentNullException(
                nameof(captureService)
            );
        _exporter =
            exporter
            ?? throw new ArgumentNullException(nameof(exporter));
        _outputSettings =
            outputSettings
            ?? throw new ArgumentNullException(
                nameof(outputSettings)
            );
        _stemIsolationService =
            stemIsolationService
            ?? throw new ArgumentNullException(
                nameof(stemIsolationService)
            );
        _dispatcher =
            dispatcher
            ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public Task<ClipRetrimResult> OpenAsync(
        ClipCatalogEntry entry,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _dispatcher.CheckAccess()
            ? Task.FromResult(OpenCore(entry, cancellationToken))
            : _dispatcher
                .InvokeAsync(
                    () => OpenCore(entry, cancellationToken)
                )
                .Task;
    }

    public void CloseAll()
    {
        foreach (var window in _windows.Values.ToArray())
        {
            window.Close();
        }

        _windows.Clear();
    }

    private ClipRetrimResult OpenCore(
        ClipCatalogEntry entry,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_windows.TryGetValue(entry.Id, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            return ClipRetrimResult.Opened;
        }

        var snapshot = _captureService.Query(
            entry.TrackInstanceId
        );
        if (!ClipRetrimAvailability.IsAvailable(entry, snapshot))
        {
            return ClipRetrimResult.BufferUnavailable;
        }

        var session = new TrimSession
        {
            Track = new NowPlayingTrack
            {
                InstanceId = entry.TrackInstanceId,
                Title = entry.SourceTitle,
                Artist = entry.SourceArtist,
                Album = entry.SourceAlbum,
                Duration = snapshot.AvailableEnd,
                AlbumArt = entry.AlbumArt,
            },
            Snapshot = snapshot,
            InitialSelectionStart = entry.TrimStart,
            InitialSelectionEnd = entry.TrimEnd,
        };
        var preview = new AudioPreviewPlayer(_dispatcher);
        var viewModel = new TrimViewModel(
            session,
            _exporter,
            preview,
            _outputSettings,
            _stemIsolationService
        );
        var window = new TrimWindow(viewModel);
        _windows.Add(entry.Id, window);
        window.Closed += (_, _) => _windows.Remove(entry.Id);
        window.Show();
        window.Activate();
        return ClipRetrimResult.Opened;
    }
}
