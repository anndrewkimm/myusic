using Hookline.NowPlaying.SystemMedia;

namespace Hookline.NowPlaying;

internal static class SpotifySessionFilter
{
    private const string DesktopSourceId = "Spotify.exe";
    private const string StoreSourceIdPrefix = "SpotifyAB.SpotifyMusic_";
    private const string StoreSourceIdSuffix = "!Spotify";

    public static bool IsSpotifySource(string? sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return false;
        }

        if (
            string.Equals(
                sourceAppUserModelId,
                DesktopSourceId,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return true;
        }

        return sourceAppUserModelId.StartsWith(
                StoreSourceIdPrefix,
                StringComparison.OrdinalIgnoreCase
            )
            && sourceAppUserModelId.EndsWith(
                StoreSourceIdSuffix,
                StringComparison.OrdinalIgnoreCase
            );
    }

    public static ISystemMediaSession? Select(
        IReadOnlyList<ISystemMediaSession> sessions
    )
    {
        ISystemMediaSession? fallback = null;

        foreach (var session in sessions)
        {
            if (!IsSpotifySource(session.SourceAppUserModelId))
            {
                continue;
            }

            fallback ??= session;

            if (session.GetPlaybackStatus() == SystemPlaybackStatus.Playing)
            {
                return session;
            }
        }

        return fallback;
    }
}
