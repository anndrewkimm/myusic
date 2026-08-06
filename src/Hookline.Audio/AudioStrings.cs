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

    public const string EmptyExport =
        "Select a buffered part of the track before exporting.";

    public const string UnsupportedExportFormat =
        "MP3 export currently requires 16-bit PCM audio.";

    public const string ImportFileMissing =
        "The selected media file no longer exists.";

    public const string UnsupportedImportFormat =
        "Hookline can import MP3, WAV, M4A, AAC, WMA, MP4, MKV, and WebM files.";

    public const string ImportTooLong =
        "The selected audio is longer than the 30-minute import limit.";

    public const string ImportTooLarge =
        "The selected audio exceeds the 320 MB decoded-audio limit.";

    public const string ImportHasNoAudio =
        "The selected file does not contain decodable audio.";

    public const string ImportDecodeFailed =
        "Windows could not decode audio from this file. It may be corrupt or use an unsupported audio codec.";

    public const string UrlImportInvalidUrl =
        "Enter a valid link to a single YouTube video.";

    public const string UrlImportSingleVideoOnly =
        "Hookline imports one video at a time. Paste a single video link, not a playlist or channel.";

    public const string UrlImportVideoUnavailable =
        "This video is unavailable, private, age-restricted, or blocked in your region.";

    public const string UrlImportNoAudio =
        "This video does not have a supported audio-only stream.";

    public const string UrlImportNetworkFailed =
        "Hookline could not reach YouTube. Check your connection and try again.";

    public const string UrlImportFetchFailed =
        "Hookline couldn't fetch this video. YouTube may have changed how its videos are delivered.";

    public const string UnsupportedEffectsFormat =
        "Clip effects currently require 16-bit PCM audio.";

    public const string InvalidEffectSpeed =
        "The speed multiplier is outside the supported range.";

    public const string InvalidLoopCount =
        "The loop count is outside the supported range.";

    public const string InvalidReverbAmount =
        "The reverb amount is outside the supported range.";

    public const string InvalidRotationRate =
        "The stereo rotation rate is outside the supported range.";

    public const string InvalidEditEffectPreset =
        "None and Custom are edit-effect states, not selectable presets.";

    public const string InvalidEqualizerBandCount =
        "An equalizer curve must contain exactly 10 bands.";

    public const string InvalidEqualizerGain =
        "Equalizer gains must be finite values from -12 dB through +12 dB.";

    public const string InvalidEqualizerPreset =
        "Custom is an equalizer state, not a selectable preset.";

    public const string StemModelIntegrityFailed =
        "The downloaded stem model failed its integrity check. Try the download again.";

    public const string StemModelUnavailable =
        "The stem model is not downloaded or did not pass its integrity check.";

    public const string UnsupportedStemFormat =
        "Stem isolation requires 44.1 kHz, 16-bit stereo audio.";

    public const string StemSelectionTooLong =
        "Stem isolation supports selections up to 5 minutes.";

    public const string InvalidStemOutput =
        "The stem model returned audio in an unexpected format.";

    public const string SegmentedSelectionEmpty =
        "A segmented clip must contain at least one segment.";

    public const string InvalidSegmentTimeline =
        "Clip segments must be contiguous and cover the selected timeline.";

    public const string SegmentedStemMixUnavailable =
        "Separate the stems before applying a per-segment stem mix.";

    public const string SegmentedOutputTooLarge =
        "The combined segment effects produce too much audio to render.";

    public const string MixSourceEmpty =
        "Choose two clips with audio before mixing.";

    public const string MixFormatMismatch =
        "Both mix sources must use the same audio format.";

    public const string MixedOutputTooLong =
        "A mixed clip cannot be longer than 5 minutes. Trim one of the sources and try again.";

    public const string SequentialMixSourceTooShort =
        "Each source needs at least 1.5 seconds of audio for the A then B crossfade.";
}
