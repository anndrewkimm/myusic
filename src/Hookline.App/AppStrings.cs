namespace Hookline.App;

public static class AppStrings
{
    public const string AppName = "Hookline";
    public const string WindowTitle = "Hookline — capture a moment";
    public const string Close = "Close";
    public const string NoCurrentTrack =
        "Play something in Spotify, then try again.";
    public const string OpenFailed = "Hookline could not open the trim window.";
    public const string CaptureUnavailable =
        "Audio capture is unavailable. Open Hookline again after Spotify is ready.";
    public const string HotkeyUnavailable =
        "Ctrl+Alt+H is already in use. Open Hookline from the tray icon.";
    public const string TrayTooltip = "Hookline — Ctrl+Alt+H to capture";
    public const string TrayOpen = "Capture a moment    Ctrl+Alt+H";
    public const string TrayImport = "Import audio file...";
    public const string TrayLibrary = "Open clip library";
    public const string TrayExit = "Exit Hookline";
    public const string TrayStatusTitle = "Hookline";
    public const string ChooseAudioFile =
        "Choose an audio file to trim";
    public const string AudioFileFilter =
        "Audio files (*.mp3;*.wav;*.m4a;*.aac;*.wma)|*.mp3;*.wav;*.m4a;*.aac;*.wma|All files (*.*)|*.*";
    public const string ImportAlreadyRunning =
        "Hookline is already importing an audio file.";
    public const string ImportFailed =
        "Audio import failed: {0}";
    public const string TrackFallback = "Untitled track";
    public const string ArtistFallback = "Unknown artist";
    public const string EmptyTime = "—";
    public const string Start = "START";
    public const string End = "END";
    public const string Selection = "SELECTION";
    public const string Earlier = "−";
    public const string Later = "+";
    public const string FineAdjustHint =
        "Drag to select · drag either edge to refine · arrows nudge 0.1s · Shift+arrows nudge 1s";
    public const string NoBufferedAudio =
        "No buffered audio yet — leave the track playing for a moment.";
    public const string Now = "NOW";
    public const string Preview = "Preview";
    public const string StopPreview = "Stop preview";
    public const string Export = "Export MP3";
    public const string Exporting = "Exporting…";
    public const string Effects = "EFFECTS";
    public const string Speed = "Speed";
    public const string BassBoost = "Bass boost";
    public const string Loop = "Loop";
    public const string EffectOff = "Off";
    public const string Equalizer = "Equalizer";
    public const string EqualizerFlat = "Flat";
    public const string EqualizerTrebleBoost = "Treble boost";
    public const string EqualizerVocal = "Vocal";
    public const string EqualizerBright = "Bright";
    public const string EqualizerMellow = "Mellow";
    public const string EqualizerCustom = "Custom";
    public const string EqualizerTune = "Tune EQ";
    public const string EqualizerHide = "Hide EQ";
    public const string EqualizerBandDescription =
        "{0} equalizer band";
    public const string EffectsLimitHint =
        "Up to 64 repeats. Effects can extend a clip to 5 minutes; longer original selections stay intact.";
    public const string StemIsolation = "STEM ISOLATION";
    public const string IsolateStems = "Isolate stems...";
    public const string StemIsolationSlowHint =
        "Slow local processing - usually takes several seconds or longer";
    public const string SixStemExperimental =
        "6 stems: add Guitar + Piano (experimental, lower quality)";
    public const string StemExperimentalHint =
        "Guitar and piano can contain bleeding and artifacts. Four stems gives the more reliable result.";
    public const string CancelStemIsolation = "Cancel";
    public const string CheckingStemModel =
        "Checking the local stem model...";
    public const string DownloadingStemModel =
        "Downloading the stem model...";
    public const string LoadingStemModel =
        "Loading the model and isolating stems...";
    public const string CancelingStemIsolation =
        "Canceling stem isolation...";
    public const string StemIsolationCanceled =
        "Stem isolation canceled.";
    public const string StemIsolationReady =
        "Stems are ready. Adjust the volumes, preview, then export.";
    public const string StemIsolationFailed =
        "Stem isolation failed: {0}";
    public const string StemSelectionTooLong =
        "Stem isolation supports selections up to 5 minutes. Trim a shorter section and try again.";
    public const string DownloadStemModelTitle =
        "Download the stem model?";
    public const string DownloadStemModelPrompt =
        "{0} needs a one-time {1} download. Processing then runs locally on this PC and may take several seconds or longer per clip.\n\nDownload it now?";
    public const string StemVocals = "Vocals";
    public const string StemBass = "Bass";
    public const string StemDrums = "Drums";
    public const string StemOther = "Other";
    public const string StemGuitar = "Guitar";
    public const string StemPiano = "Piano";
    public const string ExcludedWarning =
        "This selection crosses a paused or excluded span. Hookline will omit that span.";
    public const string OutputFolder = "SAVES TO";
    public const string ChangeFolder = "Change…";
    public const string ChooseOutputFolder = "Choose where Hookline saves clips";
    public const string SpotifyLocalFilesHint =
        "Spotify isn't watching this folder yet. Add it once in Spotify Settings → Local Files → Add a source to see new clips there.";
    public const string DismissSpotifyLocalFilesHint = "Got it";
    public const string SpotifyHintDismissFailed =
        "The Spotify Local Files hint could not be dismissed: {0}";
    public const string SelectFirst =
        "Drag across the waveform to select a clip first.";
    public const string SelectionHasNoAudio =
        "That selection contains no captured audio.";
    public const string PreviewFailed = "Preview failed: {0}";
    public const string ExportSucceeded = "Saved {0}";
    public const string ExportFailed = "Export failed: {0}";
    public const string FolderChangeFailed =
        "The output folder could not be saved: {0}";
    public const string AlbumArtDescription = "Album artwork";
    public const string NudgeStartEarlier = "Move start earlier";
    public const string NudgeStartLater = "Move start later";
    public const string NudgeEndEarlier = "Move end earlier";
    public const string NudgeEndLater = "Move end later";
    public const string LibraryWindowTitle = "Hookline — clip library";
    public const string LibraryHeading = "Your clips";
    public const string LibrarySubtitle =
        "The moments you kept, ready to play again.";
    public const string CatalogSortLabel = "SORT";
    public const string CatalogSortRecent = "Most recent";
    public const string CatalogSortArtist = "By artist";
    public const string CatalogEmpty =
        "No clips yet. Capture a moment and it will appear here automatically.";
    public const string CatalogPlay = "Play";
    public const string CatalogStop = "Stop";
    public const string CatalogRename = "Rename";
    public const string CatalogSave = "Save";
    public const string CatalogCancel = "Cancel";
    public const string CatalogDelete = "Delete";
    public const string CatalogRetrim = "Re-trim";
    public const string CatalogReveal = "Show in folder";
    public const string CatalogMissing = "FILE MISSING";
    public const string CatalogEntryNotFound =
        "That catalog entry no longer exists.";
    public const string CatalogUnavailable =
        "The clip library is unavailable. Restart Hookline to try again.";
    public const string CatalogRenameRollbackFailed =
        "The title update could not be rolled back cleanly.";
    public const string CatalogDeleteCleanupFailed =
        "The clip left the catalog, but its temporary deletion file could not be removed.";
    public const string CatalogFileMissing =
        "The clip file is missing: {0}";
    public const string CatalogRegistrationFailed =
        "The MP3 was not kept because it could not be added to the catalog: {0}";
    public const string CatalogRegistrationCleanupFailed =
        "The MP3 could not be cataloged or removed. It may still be at {0}: {1}";
    public const string CatalogLoadFailed =
        "The clip library could not be loaded: {0}";
    public const string CatalogOpenFailed =
        "The clip library could not be opened: {0}";
    public const string CatalogPlayFailed =
        "Playback failed: {0}";
    public const string CatalogRenameFailed =
        "Rename failed: {0}";
    public const string CatalogRenameRequired =
        "Enter a name for this clip.";
    public const string CatalogDeleteConfirmTitle = "Delete this clip?";
    public const string CatalogDeleteConfirm =
        "Delete “{0}” from the library and permanently remove its MP3 file?";
    public const string CatalogDeleteFailed =
        "Delete failed: {0}";
    public const string CatalogRetrimUnavailable =
        "The original rolling-buffer audio is no longer available for this clip.";
    public const string CatalogRetrimFailed =
        "Re-trim failed: {0}";
    public const string CatalogRevealFailed =
        "Show in folder failed: {0}";
    public const string CatalogDatabaseFailed =
        "The local clip catalog could not be opened: {0}";
    public const string CatalogTitleUpdated = "Clip renamed.";
    public const string CatalogDeleted = "Clip deleted.";
}
