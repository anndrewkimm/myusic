using Hookline.Audio;
using Hookline.NowPlaying;

namespace Hookline.App.Catalog;

internal sealed class WorkspaceClipRetrimLauncher :
    IClipRetrimLauncher
{
    private readonly HooklineAudioCaptureService _captureService;
    private readonly LocalAudioFileImporter _importer;
    private readonly Action<TrimSession> _openSession;
    private readonly HashSet<Guid> _opening = [];

    public WorkspaceClipRetrimLauncher(
        HooklineAudioCaptureService captureService,
        LocalAudioFileImporter importer,
        Action<TrimSession> openSession
    )
    {
        _captureService =
            captureService
            ?? throw new ArgumentNullException(
                nameof(captureService)
            );
        _importer =
            importer
            ?? throw new ArgumentNullException(nameof(importer));
        _openSession =
            openSession
            ?? throw new ArgumentNullException(nameof(openSession));
    }

    public async Task<ClipRetrimResult> OpenAsync(
        ClipCatalogEntry entry,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!_opening.Add(entry.Id))
        {
            return ClipRetrimResult.Opened;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = _captureService.Query(
                entry.TrackInstanceId
            );
            TrimSession session;
            if (ClipRetrimAvailability.IsAvailable(entry, snapshot))
            {
                session = new TrimSession
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
            _openSession(session);
            return ClipRetrimResult.Opened;
        }
        finally
        {
            _opening.Remove(entry.Id);
        }
    }
}
