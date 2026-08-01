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
            _settings
        );
        viewModel.SetSource(
            MixSourceSlot.First,
            CreateSource("First song", "First artist", 1_000)
        );
        viewModel.SetSource(
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
            _settings
        );
        var source = CreateSource("Thickened", "Artist", 4_000);
        viewModel.SetSource(MixSourceSlot.First, source);
        viewModel.SetSource(MixSourceSlot.Second, source);

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
            _settings
        );
        var source = CreateSource("Original", "Artist", 100);
        viewModel.SetSource(MixSourceSlot.First, source);
        viewModel.SetSource(MixSourceSlot.Second, source);
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
            _settings
        );
        var source = CreateSource("Busy", "Artist", 100);
        viewModel.SetSource(MixSourceSlot.First, source);
        viewModel.SetSource(MixSourceSlot.Second, source);
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
    )
    {
        var format = new PcmAudioFormat(44_100, 16, 2);
        var audio = new byte[4 * sizeof(short)];
        for (var index = 0; index < 4; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(
                audio.AsSpan(
                    index * sizeof(short),
                    sizeof(short)
                ),
                sample
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
