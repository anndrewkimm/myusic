using Hookline.Audio;
using Hookline.NowPlaying;

namespace Hookline.App;

public static class ImportedAudioTrimSessionFactory
{
    public static TrimSession Create(ImportedAudioFile imported)
    {
        ArgumentNullException.ThrowIfNull(imported);
        return new TrimSession
        {
            Track = new NowPlayingTrack
            {
                InstanceId = imported.Snapshot.TrackInstanceId,
                Title = imported.Metadata.Title,
                Artist = imported.Metadata.Artist,
                Album = imported.Metadata.Album,
                Duration = imported.Snapshot.Duration,
                AlbumArt = imported.Metadata.AlbumArt,
            },
            Snapshot = imported.Snapshot,
        };
    }
}
