using System.Globalization;

namespace Hookline.NowPlaying.Debug;

internal static class DebugStrings
{
    public const string Header = "Hookline now-playing debug viewer";
    public const string ExitHint = "Open Spotify and change tracks. Press Ctrl+C to exit.";

    public static string LogLine(string message) =>
        $"[{DateTimeOffset.Now:HH:mm:ss.fff}] {message}";

    public static string StatusMessage(NowPlayingWatcherStatus status) =>
        status switch
        {
            NowPlayingWatcherStatus.Starting => "Starting watcher",
            NowPlayingWatcherStatus.WaitingForSource =>
                "Spotify session not found; waiting",
            NowPlayingWatcherStatus.Monitoring => "Monitoring Spotify",
            NowPlayingWatcherStatus.Error => "Watcher error",
            _ => "Watcher stopped",
        };

    public static string PlaybackMessage(PlaybackState state) =>
        $"Playback: {state}";

    public static string TrackMessage(NowPlayingTrack track)
    {
        var duration = track.Duration?.ToString(@"mm\:ss", CultureInfo.InvariantCulture)
            ?? "unknown duration";
        var adMarker = track.IsLikelyAd ? " [likely ad]" : string.Empty;
        var albumArt = track.AlbumArt.IsEmpty
            ? "no album art"
            : $"{track.AlbumArt.Length} album-art bytes";

        return $"Track #{track.InstanceId}: {track.Title} — {track.Artist} "
            + $"| {track.Album} | {duration} | {albumArt}{adMarker}";
    }

    public static string FatalError(string detail) =>
        $"Unable to start the now-playing watcher: {detail}";
}
