using System.Buffers.Binary;
using System.IO;
using Hookline.App.Catalog;
using Hookline.App.Mixing;
using Hookline.Audio;

namespace Hookline.App.Tests;

public sealed class MixWindowViewModelTests : IDisposable
{
    private readonly string _temporaryDirectory;
    private readonly ClipCatalogService _catalog;
    private readonly OutputFolderSettings _settings;

    public MixWindowViewModelTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-mix-tests-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(_temporaryDirectory);
        var repository = new ClipCatalogRepository(
            Path.Combine(_temporaryDirectory, "clips.db")
        );
        repository.Initialize();
        _catalog = new ClipCatalogService(repository);
        _settings = new OutputFolderSettings(
            Path.Combine(_temporaryDirectory, "settings.json"),
            new NoSpotifySourceDetector(),
            Path.Combine(_temporaryDirectory, "exports")
        );
    }

    [Fact]
    public async Task SelectedSourcesGetEditableDefaultsAndExport()
    {
        var exporter = new RecordingExporter(
            Path.Combine(_temporaryDirectory, "mix.mp3")
        );
        using var viewModel = new MixWindowViewModel(
            _catalog,
            new LocalAudioFileImporter(),
            exporter,
            _settings,
            new RecordingPreviewPlayer()
        );
        await viewModel.SetSourceAsync(
            MixSourceSlot.First,
            CreateSource("First song", "First artist", 1_000)
        );
        await viewModel.SetSourceAsync(
            MixSourceSlot.Second,
            CreateSource("Second song", "Second artist", 2_000)
        );

        Assert.Equal("First song / Second song", viewModel.ExportTitle);
        Assert.Equal(
            "First artist / Second artist",
            viewModel.ExportArtist
        );
        Assert.True(viewModel.CanExport);

        viewModel.ExportTitle = "My mashup";
        viewModel.ExportArtist = "A & B";
        viewModel.FirstVolumePercent = 50;
        viewModel.SecondVolumePercent = 100;
        await viewModel.ExportAsync();

        Assert.NotNull(exporter.Selection);
        Assert.Equal("My mashup", exporter.Metadata?.Title);
        Assert.Equal("A & B", exporter.Metadata?.Artist);
        Assert.Equal(
            [2_500, 2_500, 2_500, 2_500],
            ReadSamples(exporter.Selection!.Audio.Span)
        );
        Assert.Contains("mix.mp3", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SameSourceCanFillBothSlots()
    {
        var exporter = new RecordingExporter(
            Path.Combine(_temporaryDirectory, "self-mix.mp3")
        );
        using var viewModel = new MixWindowViewModel(
            _catalog,
            new LocalAudioFileImporter(),
            exporter,
            _settings,
            new RecordingPreviewPlayer()
        );
        var source = CreateSource("Thickened", "Artist", 4_000);
        await viewModel.SetSourceAsync(MixSourceSlot.First, source);
        await viewModel.SetSourceAsync(MixSourceSlot.Second, source);

        await viewModel.ExportAsync();

        Assert.NotNull(exporter.Selection);
        Assert.All(
            ReadSamples(exporter.Selection!.Audio.Span),
            sample => Assert.Equal(8_000, sample)
        );
    }

    [Fact]
    public async Task IndependentEditorRendersFeedEachMixSource()
    {
        var exporter = new RecordingExporter(
            Path.Combine(_temporaryDirectory, "edited-mix.mp3")
        );
        using var viewModel = new MixWindowViewModel(
            _catalog,
            new LocalAudioFileImporter(),
            exporter,
            _settings,
            new RecordingPreviewPlayer()
        );
        var source = CreateSource("Original", "Artist", 100);
        await viewModel.SetSourceAsync(MixSourceSlot.First, source);
        await viewModel.SetSourceAsync(MixSourceSlot.Second, source);
        var firstRenderCount = 0;
        var secondRenderCount = 0;
        viewModel.SetSourceRenderer(
            MixSourceSlot.First,
            _ =>
            {
                firstRenderCount++;
                return Task.FromResult<AudioBufferSnapshot?>(
                    CreateSource("Edited A", "Artist", 1_000)
                        .Snapshot
                );
            },
            () => true
        );
        viewModel.SetSourceRenderer(
            MixSourceSlot.Second,
            _ =>
            {
                secondRenderCount++;
                return Task.FromResult<AudioBufferSnapshot?>(
                    CreateSource("Edited B", "Artist", 2_000)
                        .Snapshot
                );
            },
            () => true
        );

        await viewModel.ExportAsync();

        Assert.Equal(1, firstRenderCount);
        Assert.Equal(1, secondRenderCount);
        Assert.NotNull(exporter.Selection);
        Assert.All(
            ReadSamples(exporter.Selection!.Audio.Span),
            sample => Assert.Equal(3_000, sample)
        );
    }

    [Fact]
    public async Task BusySourceEditorBlocksMixExport()
    {
        var exporter = new RecordingExporter(
            Path.Combine(_temporaryDirectory, "blocked-mix.mp3")
        );
        using var viewModel = new MixWindowViewModel(
            _catalog,
            new LocalAudioFileImporter(),
            exporter,
            _settings,
            new RecordingPreviewPlayer()
        );
        var source = CreateSource("Busy", "Artist", 100);
        await viewModel.SetSourceAsync(MixSourceSlot.First, source);
        await viewModel.SetSourceAsync(MixSourceSlot.Second, source);
        viewModel.SetSourceRenderer(
            MixSourceSlot.First,
            _ => Task.FromResult<AudioBufferSnapshot?>(source.Snapshot),
            () => false
        );
        viewModel.SetSourceRenderer(
            MixSourceSlot.Second,
            _ => Task.FromResult<AudioBufferSnapshot?>(source.Snapshot),
            () => true
        );

        Assert.False(viewModel.CanExport);

        await viewModel.ExportAsync();

        Assert.Null(exporter.Selection);
        Assert.Equal(
            AppStrings.WorkspaceMixSourceBusy,
            viewModel.StatusMessage
        );
    }

    [Fact]
    public async Task MashupRecipeSeparatesAssignsRolesAndSwapsSources()
    {
        using var viewModel = new MixWindowViewModel(
            _catalog,
            new LocalAudioFileImporter(),
            new RecordingExporter(
                Path.Combine(_temporaryDirectory, "mashup.mp3")
            ),
            _settings,
            new RecordingPreviewPlayer()
        );
        await viewModel.SetSourceAsync(
            MixSourceSlot.First,
            CreateSource("Vocal song", "Singer", 1_000)
        );
        await viewModel.SetSourceAsync(
            MixSourceSlot.Second,
            CreateSource("Backing song", "Band", 2_000)
        );
        var firstEditor = new RecordingEditor(
            CreateSource("Edited vocals", "Singer", 1_000).Snapshot
        );
        var secondEditor = new RecordingEditor(
            CreateSource("Edited backing", "Band", 2_000).Snapshot
        );
        viewModel.SetSourceEditor(
            MixSourceSlot.First,
            firstEditor.CreateIntegration()
        );
        viewModel.SetSourceEditor(
            MixSourceSlot.Second,
            secondEditor.CreateIntegration()
        );

        await viewModel.SelectRecipeAsync(
            MixRecipe.VocalsAndInstrumentalMashup
        );

        Assert.True(viewModel.IsMashupRecipe);
        Assert.Equal(AppStrings.MixVocalsSource, viewModel.FirstSourceLabel);
        Assert.Equal(
            AppStrings.MixInstrumentalSource,
            viewModel.SecondSourceLabel
        );
        Assert.Equal(1, firstEditor.EnsureCount);
        Assert.Equal(1, secondEditor.EnsureCount);
        Assert.Equal(MixStemRole.VocalsOnly, firstEditor.LastRole);
        Assert.Equal(
            MixStemRole.InstrumentalOnly,
            secondEditor.LastRole
        );
        Assert.True(viewModel.CanExport);

        var swapped = false;
        viewModel.SourcesSwapped += (_, _) => swapped = true;
        viewModel.SwapSources();

        Assert.True(swapped);
        Assert.Equal("Backing song", viewModel.FirstSourceTitle);
        Assert.Equal("Vocal song", viewModel.SecondSourceTitle);
        Assert.Equal(MixStemRole.VocalsOnly, secondEditor.LastRole);
        Assert.Equal(
            MixStemRole.InstrumentalOnly,
            firstEditor.LastRole
        );

        await viewModel.SelectRecipeAsync(MixRecipe.Custom);
        Assert.Equal(MixStemRole.VocalsOnly, secondEditor.LastRole);
        Assert.Equal(
            MixStemRole.InstrumentalOnly,
            firstEditor.LastRole
        );
        await viewModel.SelectRecipeAsync(MixRecipe.Sequential);
        Assert.Equal(MixStemRole.FullMix, firstEditor.LastRole);
        Assert.Equal(MixStemRole.FullMix, secondEditor.LastRole);
        await viewModel.SelectRecipeAsync(
            MixRecipe.VocalsAndInstrumentalMashup
        );
        Assert.Equal(MixStemRole.VocalsOnly, secondEditor.LastRole);
        Assert.Equal(
            MixStemRole.InstrumentalOnly,
            firstEditor.LastRole
        );
    }

    [Fact]
    public async Task SequentialRecipeExportsAThenBWithEqualPowerOverlap()
    {
        var exporter = new RecordingExporter(
            Path.Combine(_temporaryDirectory, "sequential.mp3")
        );
        using var viewModel = new MixWindowViewModel(
            _catalog,
            new LocalAudioFileImporter(),
            exporter,
            _settings,
            new RecordingPreviewPlayer()
        );
        var format = new PcmAudioFormat(2, 16, 1);
        await viewModel.SetSourceAsync(
            MixSourceSlot.First,
            CreateSource(
                "First",
                "Artist A",
                format,
                [10_000, 10_000, 10_000, 10_000]
            )
        );
        await viewModel.SetSourceAsync(
            MixSourceSlot.Second,
            CreateSource(
                "Second",
                "Artist B",
                format,
                [10_000, 10_000, 10_000, 10_000]
            )
        );

        await viewModel.SelectRecipeAsync(MixRecipe.Sequential);
        await viewModel.ExportAsync();

        Assert.NotNull(exporter.Selection);
        Assert.Equal(
            [10_000, 10_000, 14_142, 10_000, 10_000],
            ReadSamples(exporter.Selection!.Audio.Span)
        );
    }

    [Fact]
    public async Task PreviewAndExportUseTheSameRenderedMixBytes()
    {
        var exporter = new RecordingExporter(
            Path.Combine(_temporaryDirectory, "previewed.mp3")
        );
        var preview = new RecordingPreviewPlayer();
        using var viewModel = new MixWindowViewModel(
            _catalog,
            new LocalAudioFileImporter(),
            exporter,
            _settings,
            preview
        );
        await viewModel.SetSourceAsync(
            MixSourceSlot.First,
            CreateSource("First", "Artist A", 1_000)
        );
        await viewModel.SetSourceAsync(
            MixSourceSlot.Second,
            CreateSource("Second", "Artist B", 2_000)
        );

        await viewModel.PreviewAsync();
        var previewBytes = preview.Snapshot?.Audio.ToArray();
        await viewModel.ExportAsync();

        Assert.NotNull(previewBytes);
        Assert.NotNull(exporter.Selection);
        Assert.Equal(previewBytes, exporter.Selection!.Audio.ToArray());
    }

    [Fact]
    public async Task MashupPreparationGatesPreviewAndExportUntilBothFinish()
    {
        using var viewModel = new MixWindowViewModel(
            _catalog,
            new LocalAudioFileImporter(),
            new RecordingExporter(
                Path.Combine(_temporaryDirectory, "gated.mp3")
            ),
            _settings,
            new RecordingPreviewPlayer()
        );
        var source = CreateSource("Source", "Artist", 1_000);
        await viewModel.SetSourceAsync(MixSourceSlot.First, source);
        await viewModel.SetSourceAsync(MixSourceSlot.Second, source);
        var firstReady = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var secondReady = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var firstHasStems = false;
        var secondHasStems = false;
        viewModel.SetSourceEditor(
            MixSourceSlot.First,
            CreateDelayedEditor(
                source.Snapshot,
                firstReady.Task,
                () => firstHasStems,
                () => firstHasStems = true
            )
        );
        viewModel.SetSourceEditor(
            MixSourceSlot.Second,
            CreateDelayedEditor(
                source.Snapshot,
                secondReady.Task,
                () => secondHasStems,
                () => secondHasStems = true
            )
        );

        var selecting = viewModel.SelectRecipeAsync(
            MixRecipe.VocalsAndInstrumentalMashup
        );
        await Task.Yield();

        Assert.True(viewModel.IsPreparingMashup);
        Assert.False(viewModel.CanPreview);
        Assert.False(viewModel.CanExport);

        firstReady.SetResult(true);
        secondReady.SetResult(true);
        await selecting;

        Assert.False(viewModel.IsPreparingMashup);
        Assert.True(viewModel.CanPreview);
        Assert.True(viewModel.CanExport);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static ImportedAudioFile CreateSource(
        string title,
        string artist,
        short sample
    ) =>
        CreateSource(
            title,
            artist,
            new PcmAudioFormat(44_100, 16, 2),
            [sample, sample, sample, sample]
        );

    private static ImportedAudioFile CreateSource(
        string title,
        string artist,
        PcmAudioFormat format,
        IReadOnlyList<short> samples
    )
    {
        var audio = new byte[samples.Count * sizeof(short)];
        for (var index = 0; index < samples.Count; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(
                audio.AsSpan(
                    index * sizeof(short),
                    sizeof(short)
                ),
                samples[index]
            );
        }

        var duration = format.GetDuration(audio.Length);
        return new ImportedAudioFile
        {
            SourcePath = $"{title}.mp3",
            Metadata = new ClipExportMetadata
            {
                Title = title,
                Artist = artist,
                Album = string.Empty,
            },
            Snapshot = new AudioBufferSnapshot
            {
                TrackInstanceId = 10,
                Format = format,
                Audio = audio,
                RequestedStart = TimeSpan.Zero,
                RequestedEnd = duration,
                AvailableStart = TimeSpan.Zero,
                AvailableEnd = duration,
                IncludedRanges =
                [
                    new AudioTimeRange(TimeSpan.Zero, duration),
                ],
            },
        };
    }

    private static short[] ReadSamples(ReadOnlySpan<byte> audio)
    {
        var samples = new short[audio.Length / sizeof(short)];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] =
                BinaryPrimitives.ReadInt16LittleEndian(
                    audio.Slice(
                        index * sizeof(short),
                        sizeof(short)
                    )
                );
        }

        return samples;
    }

    private static MixSourceEditorIntegration CreateDelayedEditor(
        AudioBufferSnapshot snapshot,
        Task<bool> ready,
        Func<bool> hasStems,
        Action markReady
    ) =>
        new(
            _ => Task.FromResult<AudioBufferSnapshot?>(snapshot),
            () => true,
            async _ =>
            {
                var result = await ready;
                if (result)
                {
                    markReady();
                }

                return result;
            },
            hasStems,
            () => hasStems() ? 100 : 0,
            _ => { }
        );

    private sealed class RecordingEditor(AudioBufferSnapshot snapshot)
    {
        public int EnsureCount { get; private set; }

        public bool HasStems { get; private set; }

        public MixStemRole? LastRole { get; private set; }

        public MixSourceEditorIntegration CreateIntegration() =>
            new(
                _ => Task.FromResult<AudioBufferSnapshot?>(snapshot),
                () => true,
                _ =>
                {
                    EnsureCount++;
                    HasStems = true;
                    return Task.FromResult(true);
                },
                () => HasStems,
                () => HasStems ? 100 : 0,
                role => LastRole = role
            );
    }

    private sealed class RecordingPreviewPlayer : IAudioPreviewPlayer
    {
        public event EventHandler<AudioPreviewPositionChangedEventArgs>?
            PositionChanged;

        public event EventHandler? PlaybackStopped;

        public bool IsPlaying { get; private set; }

        public TimeSpan CurrentAudioPosition { get; private set; }

        public AudioBufferSnapshot? Snapshot { get; private set; }

        public void Play(
            AudioBufferSnapshot snapshot,
            TimeSpan resumeAt = default
        )
        {
            Snapshot = snapshot;
            CurrentAudioPosition = resumeAt;
            IsPlaying = true;
            PositionChanged?.Invoke(
                this,
                new AudioPreviewPositionChangedEventArgs(resumeAt)
            );
        }

        public void Stop()
        {
            IsPlaying = false;
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() => Stop();
    }

    private sealed class RecordingExporter(string outputPath) :
        IClipExporter
    {
        public AudioBufferSnapshot? Selection { get; private set; }

        public ClipExportMetadata? Metadata { get; private set; }

        public Task<ClipExportResult> ExportAsync(
            AudioBufferSnapshot selection,
            ClipExportMetadata metadata,
            string outputFolder,
            CancellationToken cancellationToken = default
        )
        {
            Selection = selection;
            Metadata = metadata;
            return Task.FromResult(
                new ClipExportResult
                {
                    OutputPath = outputPath,
                    Duration = selection.Duration,
                }
            );
        }
    }

    private sealed class NoSpotifySourceDetector :
        ISpotifyLocalFilesSourceDetector
    {
        public string? DetectSourceFolder() => null;
    }
}
