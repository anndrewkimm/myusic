namespace Hookline.Audio;

internal static class AudioStrings
{
    public const string NoTargetProcess =
        "Spotify's process could not be identified.";

    public const string ProcessCaptureUnavailable =
        "Spotify-only loopback capture could not be started.";

    public const string ProcessLoopbackUnsupported =
        "Process loopback requires Windows build 20348 or later.";

    public const string CaptureStalled =
        "No capture packets arrived while playback was active.";

    public const string CaptureStoppedUnexpectedly =
        "The audio capture backend stopped unexpectedly.";

    public const string NoCurrentTrack =
        "There is no current track.";

    public const string NoBufferedAudio =
        "The current track has no buffered audio.";
}
