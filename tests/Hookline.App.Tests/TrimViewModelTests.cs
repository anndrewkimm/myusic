using Hookline.Audio;
using Hookline.NowPlaying;
using System.IO;

namespace Hookline.App.Tests;

public sealed class TrimViewModelTests
{
    [Fact]
    public void SpotifySourceHintCanBeDismissedFromTrimState()
    {
        using var fixture = new ViewModelFixture(
            showSpotifyHint: true
        );

        Assert.True(
            fixture.ViewModel.ShowSpotifyLocalFilesHint
        );

        fixture.ViewModel.DismissSpotifyLocalFilesHint();

        Assert.False(
            fixture.ViewModel.ShowSpotifyLocalFilesHint
        );
    }

    [Fact]
    public void RetrimSessionOpensWithTheOriginalSelectionSeeded()
    {
        using var fixture = new ViewModelFixture(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(4)
        );

        Assert.True(fixture.ViewModel.HasSelection);
        Assert.Equal(
            TimeSpan.FromSeconds(1),
            fixture.ViewModel.SelectionStart
        );
        Assert.Equal(
            TimeSpan.FromSeconds(4),
            fixture.ViewModel.SelectionEnd
        );
    }

    [Fact]
    public void OpensWithoutSelectionAndNudgesTheFocusedEdge()
    {
        using var fixture = new ViewModelFixture();

        Assert.False(fixture.ViewModel.HasSelection);
        Assert.Equal(
            AppStrings.EmptyTime,
            fixture.ViewModel.SelectionDurationText
        );

        fixture.ViewModel.SetSelection(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5)
        );
        fixture.ViewModel.SetActiveEdge(SelectionEdge.Start);
        fixture.ViewModel.NudgeActiveEdge(
            TimeSpan.FromMilliseconds(100)
        );
        fixture.ViewModel.SetActiveEdge(SelectionEdge.End);
        fixture.ViewModel.NudgeActiveEdge(
            TimeSpan.FromSeconds(-1)
        );

        Assert.Equal(
            TimeSpan.FromSeconds(2.1),
            fixture.ViewModel.SelectionStart
        );
        Assert.Equal(
            TimeSpan.FromSeconds(4),
            fixture.ViewModel.SelectionEnd
        );
    }

    [Fact]
    public async Task GapOverlapWarnsButStillExportsIncludedAudio()
    {
        using var fixture = new ViewModelFixture();
        fixture.ViewModel.SetSelection(
            TimeSpan.FromSeconds(1.5),
            TimeSpan.FromSeconds(3.5)
        );

        Assert.True(
            fixture.ViewModel.SelectionOverlapsExcluded
        );

        await fixture.ViewModel.ExportAsync();

        Assert.NotNull(fixture.Exporter.LastSelection);
        var exported = fixture.Exporter.LastSelection!;
        Assert.Equal(TimeSpan.FromSeconds(1), exported.Duration);
        Assert.Contains(
            "clip.mp3",
            fixture.ViewModel.StatusMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void PreviewUsesTheFrozenSelectionSnapshot()
    {
        using var fixture = new ViewModelFixture();
        fixture.ViewModel.SetSelection(
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1.25)
        );

        fixture.ViewModel.TogglePreview();

        Assert.NotNull(fixture.Preview.LastSnapshot);
        var previewed = fixture.Preview.LastSnapshot!;
        Assert.Equal(
            fixture.ViewModel.Session.Track.InstanceId,
            previewed.TrackInstanceId
        );
        Assert.Equal(
            TimeSpan.FromSeconds(1),
            previewed.Duration
        );
    }

    [Fact]
    public async Task EffectsAreNeutralByDefaultAndKeepTheExistingExportPath()
    {
        using var fixture = new ViewModelFixture();
        fixture.ViewModel.SetSelection(
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1.25)
        );

        await fixture.ViewModel.ExportAsync();

        Assert.Equal(1, fixture.ViewModel.SpeedMultiplier);
        Assert.Equal(0, fixture.ViewModel.ReverbAmountPercent);
        Assert.Equal(0, fixture.ViewModel.RotationRateHertz);
        Assert.Equal(
            EditEffectPreset.None,
            fixture.ViewModel.EditEffectPreset
        );
        Assert.Equal(
            AppStrings.EditPresetNone,
            fixture.ViewModel.EditEffectPresetText
        );
        Assert.Equal(1, fixture.ViewModel.LoopCount);
        Assert.Equal(
            EqualizerPreset.Flat,
            fixture.ViewModel.EqualizerPreset
        );
        Assert.All(
            fixture.ViewModel.EqualizerBands,
            band => Assert.Equal(0, band.GainDecibels)
        );
        Assert.False(fixture.ViewModel.IsEqualizerExpanded);
        Assert.Equal("1.00×", fixture.ViewModel.SpeedMultiplierText);
        Assert.Equal(AppStrings.EffectOff, fixture.ViewModel.LoopCountText);
        Assert.Equal(
            TimeSpan.FromSeconds(1),
            fixture.Exporter.LastSelection?.Duration
        );
    }

    [Fact]
    public async Task EditPresetPreviewAndExportUseTheSameLiveOutput()
    {
        using var fixture = new ViewModelFixture();
        fixture.ViewModel.SetSelection(
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1.25)
        );
        fixture.ViewModel.ApplyEditEffectPreset(
            EditEffectPreset.SlowedReverb
        );

        fixture.ViewModel.TogglePreview();
        await fixture.ViewModel.ExportAsync();

        Assert.NotNull(fixture.Preview.LastSnapshot);
        Assert.NotNull(fixture.Exporter.LastSelection);
        Assert.True(
            fixture.Preview.LastSnapshot!.Audio.Span.SequenceEqual(
                fixture.Exporter.LastSelection!.Audio.Span
            )
        );
        Assert.Equal(
            TimeSpan.FromSeconds(3.25),
            fixture.Exporter.LastSelection.Duration
        );
    }

    [Fact]
    public void EditPresetsReplaceValuesAndManualChangesBecomeCustom()
    {
        using var fixture = new ViewModelFixture();
        fixture.ViewModel.SetSelection(
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1.25)
        );
        fixture.ViewModel.TogglePreview();

        fixture.ViewModel.ApplyEditEffectPreset(
            EditEffectPreset.SlowedReverb
        );

        Assert.Equal(
            EditEffectPreset.SlowedReverb,
            fixture.ViewModel.EditEffectPreset
        );
        Assert.Equal(0.8, fixture.ViewModel.SpeedMultiplier);
        Assert.Equal(55, fixture.ViewModel.ReverbAmountPercent);
        Assert.Equal(0, fixture.ViewModel.RotationRateHertz);
        Assert.Equal(2, fixture.Preview.PlayCallCount);

        fixture.ViewModel.ReverbAmountPercent = 50;

        Assert.Equal(
            EditEffectPreset.Custom,
            fixture.ViewModel.EditEffectPreset
        );
        Assert.Equal(
            AppStrings.EditPresetCustom,
            fixture.ViewModel.EditEffectPresetText
        );
        Assert.Equal(3, fixture.Preview.PlayCallCount);

        fixture.ViewModel.ApplyEditEffectPreset(
            EditEffectPreset.EightDAudio
        );

        Assert.Equal(
            EditEffectPreset.EightDAudio,
            fixture.ViewModel.EditEffectPreset
        );
        Assert.Equal(1, fixture.ViewModel.SpeedMultiplier);
        Assert.Equal(20, fixture.ViewModel.ReverbAmountPercent);
        Assert.Equal(0.1, fixture.ViewModel.RotationRateHertz);
        Assert.Equal(4, fixture.Preview.PlayCallCount);

        fixture.ViewModel.SpeedMultiplier = 1.1;
        Assert.Equal(
            EditEffectPreset.Custom,
            fixture.ViewModel.EditEffectPreset
        );

        fixture.ViewModel.ApplyEditEffectPreset(
            EditEffectPreset.EightDAudio
        );
        fixture.ViewModel.RotationRateHertz = 0.15;
        Assert.Equal(
            EditEffectPreset.Custom,
            fixture.ViewModel.EditEffectPreset
        );
    }

    [Fact]
    public async Task PreviewAndExportUseTheSameComposedEffects()
    {
        using var fixture = new ViewModelFixture();
        fixture.ViewModel.SetSelection(
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1.25)
        );
        fixture.ViewModel.SpeedMultiplier = 2;
        fixture.ViewModel.ApplyEqualizerPreset(
            EqualizerPreset.BassBoost
        );
        fixture.ViewModel.LoopCount = 2;

        fixture.ViewModel.TogglePreview();
        await fixture.ViewModel.ExportAsync();

        Assert.NotNull(fixture.Preview.LastSnapshot);
        Assert.NotNull(fixture.Exporter.LastSelection);
        Assert.Equal(
            fixture.Preview.LastSnapshot!.Duration,
            fixture.Exporter.LastSelection!.Duration
        );
        Assert.True(
            fixture.Preview.LastSnapshot.Audio.Span.SequenceEqual(
                fixture.Exporter.LastSelection.Audio.Span
            )
        );
        Assert.Equal(
            TimeSpan.FromMilliseconds(990),
            fixture.Exporter.LastSelection.Duration
        );
    }

    [Fact]
    public void PresetsAndManualTuningUpdateStateAndLivePreview()
    {
        using var fixture = new ViewModelFixture();
        fixture.ViewModel.SetSelection(
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1.25)
        );
        fixture.ViewModel.TogglePreview();

        fixture.ViewModel.ApplyEqualizerPreset(
            EqualizerPreset.BassBoost
        );

        Assert.Equal(
            EqualizerPreset.BassBoost,
            fixture.ViewModel.EqualizerPreset
        );
        Assert.Equal(
            AppStrings.BassBoost,
            fixture.ViewModel.EqualizerPresetText
        );
        Assert.Equal(2, fixture.Preview.PlayCallCount);
        Assert.Equal(
            [6, 6, 4, 2, 0, 0, 0, 0, 0, 0],
            fixture.ViewModel.EqualizerBands
                .Select(band => band.GainDecibels)
                .ToArray()
        );

        fixture.ViewModel.EqualizerBands[0].GainDecibels = 5;

        Assert.Equal(
            EqualizerPreset.Custom,
            fixture.ViewModel.EqualizerPreset
        );
        Assert.Equal(
            AppStrings.EqualizerCustom,
            fixture.ViewModel.EqualizerPresetText
        );
        Assert.Equal(3, fixture.Preview.PlayCallCount);

        fixture.ViewModel.ApplyEqualizerPreset(
            EqualizerPreset.Mellow
        );

        Assert.Equal(
            EqualizerPreset.Mellow,
            fixture.ViewModel.EqualizerPreset
        );
        Assert.Equal(
            [2, 2, 1, 1, 0, 0, -1, -3, -5, -6],
            fixture.ViewModel.EqualizerBands
                .Select(band => band.GainDecibels)
                .ToArray()
        );
        Assert.Equal(4, fixture.Preview.PlayCallCount);
    }

    [Fact]
    public void EqualizerManualViewStartsCollapsedAndCanToggle()
    {
        using var fixture = new ViewModelFixture();

        Assert.False(fixture.ViewModel.IsEqualizerExpanded);
        Assert.Equal(
            AppStrings.EqualizerTune,
            fixture.ViewModel.EqualizerExpandButtonText
        );

        fixture.ViewModel.ToggleEqualizerExpanded();

        Assert.True(fixture.ViewModel.IsEqualizerExpanded);
        Assert.Equal(
            AppStrings.EqualizerHide,
            fixture.ViewModel.EqualizerExpandButtonText
        );
    }

    [Fact]
    public void ChangingAnEffectResumesAtTheProportionalPosition()
    {
        using var fixture = new ViewModelFixture();
        fixture.ViewModel.SetSelection(
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1.25)
        );
        fixture.ViewModel.TogglePreview();
        fixture.Preview.CurrentAudioPosition =
            TimeSpan.FromMilliseconds(400);

        fixture.ViewModel.SpeedMultiplier = 2;

        Assert.True(fixture.Preview.IsPlaying);
        Assert.Equal(2, fixture.Preview.PlayCallCount);
        Assert.Equal(1, fixture.Preview.StopCallCount);
        Assert.Equal(
            TimeSpan.FromMilliseconds(500),
            fixture.Preview.LastSnapshot?.Duration
        );
        Assert.Equal(
            TimeSpan.FromMilliseconds(200),
            fixture.Preview.LastResumeAt
        );
    }

    [Fact]
    public void StartingPreviewFromStoppedBeginsAtZero()
    {
        using var fixture = new ViewModelFixture();
        fixture.ViewModel.SetSelection(
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1.25)
        );
        fixture.ViewModel.SpeedMultiplier = 2;

        fixture.ViewModel.TogglePreview();

        Assert.Equal(TimeSpan.Zero, fixture.Preview.LastResumeAt);
    }

    [Fact]
    public async Task ExportFailureIsAlwaysVisibleInTheWindowState()
    {
        using var fixture = new ViewModelFixture();
        fixture.Exporter.Failure = new UnauthorizedAccessException(
            "Folder is read-only."
        );
        fixture.ViewModel.SetSelection(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1)
        );

        await fixture.ViewModel.ExportAsync();

        Assert.Contains(
            "Folder is read-only.",
            fixture.ViewModel.StatusMessage,
            StringComparison.Ordinal
        );
        Assert.True(fixture.ViewModel.CanExport);
    }

    [Fact]
    public async Task StemIsolationCreatesNaturalControlsAndUsesSharedOutput()
    {
        using var fixture = new ViewModelFixture();
        fixture.ViewModel.SetSelection(
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1.25)
        );

        await fixture.ViewModel.IsolateStemsAsync(
            downloadModel: false
        );

        Assert.True(fixture.ViewModel.HasSeparatedStems);
        Assert.Equal(
            [
                StemKind.Vocals,
                StemKind.Bass,
                StemKind.Drums,
                StemKind.Other,
            ],
            fixture.ViewModel.StemVolumes
                .Select(stem => stem.Kind)
                .ToArray()
        );
        Assert.All(
            fixture.ViewModel.StemVolumes,
            stem => Assert.Equal(100, stem.VolumePercent)
        );

        fixture.ViewModel.StemVolumes[0].VolumePercent = 0;
        fixture.ViewModel.SetStemBandView(isBandView: true);
        fixture.ViewModel.ApplyEditEffectPreset(
            EditEffectPreset.SlowedReverb
        );
        fixture.ViewModel.TogglePreview();
        await fixture.ViewModel.ExportAsync();

        Assert.NotNull(fixture.Preview.LastSnapshot);
        Assert.NotNull(fixture.Exporter.LastSelection);
        Assert.True(
            fixture.Preview.LastSnapshot!.Audio.Span.SequenceEqual(
                fixture.Exporter.LastSelection!.Audio.Span
            )
        );
        Assert.Equal(
            1,
            fixture.StemService.SeparateCallCount
        );
    }

    [Fact]
    public async Task SwitchingStemViewsPreservesTheExactSharedValues()
    {
        using var fixture = new ViewModelFixture();
        fixture.ViewModel.SetSelection(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1)
        );
        await fixture.ViewModel.IsolateStemsAsync(
            downloadModel: false
        );
        var controls = fixture.ViewModel.StemVolumes.ToArray();
        controls[0].VolumePercent = 43;
        controls[1].VolumePercent = 127;

        Assert.True(fixture.ViewModel.IsStemSliderView);
        Assert.False(fixture.ViewModel.IsStemBandView);

        fixture.ViewModel.SetStemBandView(isBandView: true);

        Assert.True(fixture.ViewModel.IsStemBandView);
        Assert.False(fixture.ViewModel.IsStemSliderView);
        Assert.Equal(43, controls[0].VolumePercent);
        Assert.Equal(127, controls[1].VolumePercent);
        Assert.Same(controls[0], fixture.ViewModel.StemVolumes[0]);
        Assert.Same(controls[1], fixture.ViewModel.StemVolumes[1]);

        fixture.ViewModel.SetStemBandView(isBandView: false);

        Assert.True(fixture.ViewModel.IsStemSliderView);
        Assert.Equal(43, controls[0].VolumePercent);
        Assert.Equal(127, controls[1].VolumePercent);
    }

    [Fact]
    public async Task ExperimentalModeAddsClearlySeparateStemControls()
    {
        using var fixture = new ViewModelFixture();
        fixture.ViewModel.SetSelection(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1)
        );
        fixture.ViewModel.IsSixStemExperimental = true;

        await fixture.ViewModel.IsolateStemsAsync(
            downloadModel: false
        );

        Assert.Equal(
            StemSeparationMode.SixStemExperimental,
            fixture.StemService.LastMode
        );
        Assert.Equal(6, fixture.ViewModel.StemVolumes.Count);
        Assert.Contains(
            fixture.ViewModel.StemVolumes,
            stem => stem.Kind == StemKind.Guitar
        );
        Assert.Contains(
            fixture.ViewModel.StemVolumes,
            stem => stem.Kind == StemKind.Piano
        );
    }

    [Fact]
    public async Task FirstUseDownloadReportsThroughTheSameOperation()
    {
        using var fixture = new ViewModelFixture();
        fixture.StemService.ModelAvailable = false;
        fixture.ViewModel.SetSelection(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1)
        );

        var available =
            await fixture.ViewModel.CheckStemModelAvailableAsync();
        await fixture.ViewModel.IsolateStemsAsync(
            downloadModel: true
        );

        Assert.False(available);
        Assert.Equal(
            1,
            fixture.StemService.DownloadCallCount
        );
        Assert.True(fixture.ViewModel.HasSeparatedStems);
    }

    [Fact]
    public async Task RunningStemIsolationCanBeCanceledCleanly()
    {
        using var fixture = new ViewModelFixture();
        fixture.StemService.BlockSeparation = true;
        fixture.ViewModel.SetSelection(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1)
        );

        var operation = fixture.ViewModel.IsolateStemsAsync(
            downloadModel: false
        );
        await fixture.StemService.SeparationStarted.Task;

        fixture.ViewModel.CancelStemIsolation();
        await operation;

        Assert.False(fixture.ViewModel.IsStemSeparating);
        Assert.False(fixture.ViewModel.HasSeparatedStems);
        Assert.Equal(
            AppStrings.StemIsolationCanceled,
            fixture.ViewModel.StatusMessage
        );
    }

    private sealed class ViewModelFixture : IDisposable
    {
        private readonly string _temporaryDirectory;

        public ViewModelFixture(
            TimeSpan? initialSelectionStart = null,
            TimeSpan? initialSelectionEnd = null,
            bool showSpotifyHint = false
        )
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                $"hookline-viewmodel-{Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(_temporaryDirectory);
            Exporter = new FakeClipExporter();
            Preview = new FakeAudioPreviewPlayer();
            var settings = new OutputFolderSettings(
                Path.Combine(
                    _temporaryDirectory,
                    "settings.json"
                ),
                new NoSpotifySourceDetector(),
                _temporaryDirectory
            );
            if (!showSpotifyHint)
            {
                settings.SetOutputFolder(_temporaryDirectory);
            }
            StemService = new FakeStemIsolationService();
            ViewModel = new TrimViewModel(
                CreateSession() with
                {
                    InitialSelectionStart = initialSelectionStart,
                    InitialSelectionEnd = initialSelectionEnd,
                },
                Exporter,
                Preview,
                settings,
                StemService
            );
        }

        public TrimViewModel ViewModel { get; }

        public FakeClipExporter Exporter { get; }

        public FakeAudioPreviewPlayer Preview { get; }

        public FakeStemIsolationService StemService { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            Directory.Delete(_temporaryDirectory, recursive: true);
        }

        private static TrimSession CreateSession()
        {
            var format = new PcmAudioFormat(100, 16, 1);
            return new TrimSession
            {
                Track = new NowPlayingTrack
                {
                    InstanceId = 42,
                    Title = "Track",
                    Artist = "Artist",
                    Album = "Album",
                },
                Snapshot = new AudioBufferSnapshot
                {
                    TrackInstanceId = 42,
                    Format = format,
                    Audio = new byte[800],
                    RequestedStart = TimeSpan.Zero,
                    RequestedEnd = TimeSpan.FromSeconds(5),
                    AvailableStart = TimeSpan.Zero,
                    AvailableEnd = TimeSpan.FromSeconds(5),
                    HasGaps = true,
                    IncludedRanges =
                    [
                        new AudioTimeRange(
                            TimeSpan.Zero,
                            TimeSpan.FromSeconds(2)
                        ),
                        new AudioTimeRange(
                            TimeSpan.FromSeconds(3),
                            TimeSpan.FromSeconds(5)
                        ),
                    ],
                    ExcludedRanges =
                    [
                        new AudioTimeRange(
                            TimeSpan.FromSeconds(2),
                            TimeSpan.FromSeconds(3)
                        ),
                    ],
                },
            };
        }
    }

    private sealed class FakeClipExporter : IClipExporter
    {
        public AudioBufferSnapshot? LastSelection { get; private set; }

        public Exception? Failure { get; set; }

        public Task<ClipExportResult> ExportAsync(
            AudioBufferSnapshot selection,
            ClipExportMetadata metadata,
            string outputFolder,
            CancellationToken cancellationToken = default
        )
        {
            if (Failure is not null)
            {
                return Task.FromException<ClipExportResult>(Failure);
            }

            LastSelection = selection;
            return Task.FromResult(
                new ClipExportResult
                {
                    OutputPath = Path.Combine(
                        outputFolder,
                        "clip.mp3"
                    ),
                    Duration = selection.Duration,
                }
            );
        }
    }

    private sealed class NoSpotifySourceDetector
        : ISpotifyLocalFilesSourceDetector
    {
        public string? DetectSourceFolder() => null;
    }

    private sealed class FakeStemIsolationService
        : IStemIsolationService
    {
        public bool ModelAvailable { get; set; } = true;

        public bool BlockSeparation { get; set; }

        public int DownloadCallCount { get; private set; }

        public int SeparateCallCount { get; private set; }

        public StemSeparationMode? LastMode { get; private set; }

        public TaskCompletionSource SeparationStarted { get; } =
            new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

        public StemModelDescriptor GetModel(
            StemSeparationMode mode
        ) => StemModelCatalog.Get(mode);

        public Task<bool> IsModelAvailableAsync(
            StemSeparationMode mode,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(ModelAvailable);

        public Task DownloadModelAsync(
            StemSeparationMode mode,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default
        )
        {
            DownloadCallCount++;
            ModelAvailable = true;
            progress?.Report(1);
            return Task.CompletedTask;
        }

        public async Task<SeparatedStemSet> SeparateAsync(
            AudioBufferSnapshot selection,
            StemSeparationMode mode,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default
        )
        {
            SeparateCallCount++;
            LastMode = mode;
            SeparationStarted.TrySetResult();
            if (BlockSeparation)
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken
                );
            }

            progress?.Report(1);
            var model = GetModel(mode);
            var stems = model.Stems
                .Select(
                    (kind, index) =>
                        new SeparatedStem
                        {
                            Kind = kind,
                            Snapshot = selection with
                            {
                                Audio =
                                    index == 0
                                        ? selection.Audio
                                        : new byte[
                                            selection.Audio.Length
                                        ],
                            },
                        }
                )
                .ToArray();
            return new SeparatedStemSet
            {
                Mode = mode,
                Source = selection,
                Stems = stems,
            };
        }
    }

    private sealed class FakeAudioPreviewPlayer
        : IAudioPreviewPlayer
    {
        public event EventHandler<AudioPreviewPositionChangedEventArgs>?
            PositionChanged;

        public event EventHandler? PlaybackStopped;

        public bool IsPlaying { get; private set; }

        public TimeSpan CurrentAudioPosition { get; set; }

        public int PlayCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public AudioBufferSnapshot? LastSnapshot { get; private set; }

        public TimeSpan LastResumeAt { get; private set; }

        public void Play(
            AudioBufferSnapshot snapshot,
            TimeSpan resumeAt = default
        )
        {
            LastSnapshot = snapshot;
            LastResumeAt = resumeAt;
            PlayCallCount++;
            IsPlaying = true;
        }

        public void Stop()
        {
            StopCallCount++;
            IsPlaying = false;
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            IsPlaying = false;
            GC.KeepAlive(PositionChanged);
        }
    }
}
