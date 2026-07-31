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
    private readonly LocalAudioFileImporter _importer;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Guid, TrimWindow> _windows = [];
    private readonly HashSet<Guid> _opening = [];

    public ClipRetrimLauncher(
        HooklineAudioCaptureService captureService,
        IClipExporter exporter,
        OutputFolderSettings outputSettings,
        IStemIsolationService stemIsolationService,
        LocalAudioFileImporter importer,
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
        _importer =
            importer
            ?? throw new ArgumentNullException(nameof(importer));
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
            ? OpenCoreAsync(entry, cancellationToken)
            : _dispatcher
                .InvokeAsync(
                    () => OpenCoreAsync(entry, cancellationToken)
                )
                .Task.Unwrap();
    }

    public void CloseAll()
    {
        foreach (var window in _windows.Values.ToArray())
        {
            window.Close();
        }

        _windows.Clear();
    }

    private async Task<ClipRetrimResult> OpenCoreAsync(
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

        if (!_opening.Add(entry.Id))
        {
            return ClipRetrimResult.Opened;
        }

        try
        {
            var snapshot = _captureService.Query(
                entry.TrackInstanceId
            );
            TrimSession session;
            if (ClipRetrimAvailability.IsAvailable(entry, snapshot))
            {
                session = CreateLiveSession(entry, snapshot);
            }
            else if (
                entry.TrackInstanceId
                    == TwoSourceAudioMixer.MixedTrackInstanceId
            )
            {
                var imported = await _importer.ImportAsync(
                    entry.FilePath,
                    cancellationToken
                );
                session = ImportedAudioTrimSessionFactory.Create(
                    imported
                );
            }
            else
            {
                return ClipRetrimResult.BufferUnavailable;
            }

            cancellationToken.ThrowIfCancellationRequested();
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
        finally
        {
            _opening.Remove(entry.Id);
        }
    }

    private static TrimSession CreateLiveSession(
        ClipCatalogEntry entry,
        AudioBufferSnapshot snapshot
    ) =>
        new()
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
}
