using Hookline.Audio.Capture;

namespace Hookline.Audio.Debug;

internal static class DebugStrings
{
    public const string Header = "Hookline audio capture debug console";
    public const string Commands =
        "Commands: d [path] = dump the current track's last 30 seconds; s = status; q = quit.";
    public const string Prompt = "> ";
    public const string NoInput = "Input ended; stopping capture.";
    public const string NoBufferedAudio =
        "The current track has no buffered audio.";

    public static string Status(
        AudioCaptureStatus status,
        AudioCaptureMode? mode,
        string? detail
    )
    {
        var text =
            mode is null
                ? status.ToString()
                : $"{status} ({mode})";
        return string.IsNullOrWhiteSpace(detail)
            ? text
            : $"{text}: {detail}";
    }

    public static string Track(string title, string artist) =>
        $"Track: {title} — {artist}";

    public static string Dumped(
        string path,
        TimeSpan duration,
        bool truncated
    ) =>
        $"Wrote {duration.TotalSeconds:F1}s to {path}"
        + (truncated ? " (only the available buffered range)." : ".");

    public static string DumpFailed(string detail) =>
        $"WAV dump failed: {detail}";

    public static string UnknownCommand(string command) =>
        $"Unknown command: {command}";

    public static string Fatal(string detail) =>
        $"Audio capture could not start: {detail}";
}
