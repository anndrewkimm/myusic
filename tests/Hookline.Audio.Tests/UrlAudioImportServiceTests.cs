using System.Text;

namespace Hookline.Audio.Tests;

public sealed class UrlAudioImportServiceTests : IDisposable
{
    private readonly string _temporaryDirectory;

    public UrlAudioImportServiceTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-url-import-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Theory]
    [InlineData("not a URL", UrlAudioImportFailure.InvalidUrl)]
    [InlineData("https://example.com/watch?v=abc", UrlAudioImportFailure.InvalidUrl)]
    [InlineData("https://www.youtube.com/playlist?list=PL123", UrlAudioImportFailure.PlaylistOrChannel)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PL123", UrlAudioImportFailure.PlaylistOrChannel)]
    [InlineData("https://www.youtube.com/@some-channel", UrlAudioImportFailure.PlaylistOrChannel)]
    public async Task InvalidAndBulkUrlsAreRejectedBeforeResolution(
        string url,
        UrlAudioImportFailure failure
    )
    {
        var source = new FakeVideoAudioSource();
        var service = CreateService(source);

        var exception = await Assert.ThrowsAsync<
            UrlAudioImportException
        >(() => service.ResolveAsync(url));

        Assert.Equal(failure, exception.Failure);
        Assert.Equal(0, source.ResolveCalls);
    }

    [Fact]
    public async Task DurationLimitIsCheckedBeforeAudioDownload()
    {
        var source = new FakeVideoAudioSource
        {
            Metadata = CreateMetadata() with
            {
                Duration = TimeSpan.FromMinutes(31),
            },
        };
        var service = CreateService(source);

        var exception = await Assert.ThrowsAsync<
            LocalAudioImportException
        >(
            () =>
                service.ResolveAsync(
                    "https://youtu.be/dQw4w9WgXcQ"
                )
        );

        Assert.Equal(LocalAudioImportFailure.TooLong, exception.Failure);
        Assert.Equal(0, source.DownloadCalls);
    }

    [Fact]
    public async Task DownloadUsesExistingImporterAndMapsVideoMetadata()
    {
        var localPath = Path.Combine(
            _temporaryDirectory,
            "local.wav"
        );
        WritePcmWave(localPath);
        var importer = new LocalAudioFileImporter();
        var local = await importer.ImportAsync(localPath);
        var artwork = "thumbnail"u8.ToArray();
        var source = new FakeVideoAudioSource
        {
            Metadata = CreateMetadata() with
            {
                Title = "Video title",
                Channel = "Video channel",
                Thumbnail = artwork,
            },
        };
        var service = CreateService(source, importer);
        var progressValues = new List<double>();
        var video = await service.ResolveAsync(
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
        );

        var imported = await service.ImportAsync(
            video,
            new InlineProgress(progressValues.Add)
        );

        Assert.NotEqual(
            local.Snapshot.TrackInstanceId,
            imported.Snapshot.TrackInstanceId
        );
        Assert.True(imported.Snapshot.TrackInstanceId < 0);
        Assert.Equal("Video title", imported.Metadata.Title);
        Assert.Equal("Video channel", imported.Metadata.Artist);
        Assert.Equal(string.Empty, imported.Metadata.Album);
        Assert.Equal(artwork, imported.Metadata.AlbumArt.ToArray());
        Assert.Contains(0.5, progressValues);
        Assert.Contains(1, progressValues);
        AssertTemporaryRootIsEmpty();
    }

    [Fact]
    public async Task MissingTitleFallsBackToDownloadedFileName()
    {
        var source = new FakeVideoAudioSource
        {
            Metadata = CreateMetadata() with { Title = string.Empty },
        };
        var service = CreateService(source);
        var video = await service.ResolveAsync(
            "https://youtu.be/dQw4w9WgXcQ"
        );

        var imported = await service.ImportAsync(video);

        Assert.Equal(video.VideoId, imported.Metadata.Title);
        AssertTemporaryRootIsEmpty();
    }

    [Fact]
    public async Task CancellationDeletesPartialDownload()
    {
        var source = new FakeVideoAudioSource
        {
            CancelDownload = true,
        };
        var service = CreateService(source);
        var video = await service.ResolveAsync(
            "https://youtu.be/dQw4w9WgXcQ"
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () =>
                service.ImportAsync(
                    video,
                    cancellationToken: new CancellationToken(true)
                )
        );

        AssertTemporaryRootIsEmpty();
    }

    [Fact]
    public async Task DownloadFailureDeletesPartialDownload()
    {
        var source = new FakeVideoAudioSource
        {
            FailDownload = true,
        };
        var service = CreateService(source);
        var video = await service.ResolveAsync(
            "https://youtu.be/dQw4w9WgXcQ"
        );

        var exception = await Assert.ThrowsAsync<
            UrlAudioImportException
        >(() => service.ImportAsync(video));

        Assert.Equal(UrlAudioImportFailure.Network, exception.Failure);
        AssertTemporaryRootIsEmpty();
    }

    public void Dispose() =>
        Directory.Delete(_temporaryDirectory, recursive: true);

    private UrlAudioImportService CreateService(
        FakeVideoAudioSource source,
        LocalAudioFileImporter? importer = null
    ) => new(
        source,
        importer ?? new LocalAudioFileImporter(),
        Path.Combine(_temporaryDirectory, "downloads")
    );

    private void AssertTemporaryRootIsEmpty()
    {
        var root = Path.Combine(_temporaryDirectory, "downloads");
        Assert.True(
            !Directory.Exists(root)
            || Directory.GetFileSystemEntries(root).Length == 0
        );
    }

    private static UrlVideoMetadata CreateMetadata() =>
        new()
        {
            SourceUrl = new Uri(
                "https://youtu.be/dQw4w9WgXcQ"
            ),
            VideoId = "dQw4w9WgXcQ",
            Title = "Title",
            Channel = "Channel",
            Duration = TimeSpan.FromMinutes(3),
            AudioFileExtension = ".wav",
        };

    private static void WritePcmWave(string path)
    {
        const int sampleRate = 44_100;
        const short channels = 2;
        const short bitsPerSample = 16;
        const int audioBytes = sampleRate * channels * 2 / 10;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(
            stream,
            Encoding.ASCII,
            leaveOpen: false
        );
        writer.Write("RIFF"u8);
        writer.Write(36 + audioBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(audioBytes);
        writer.Write(new byte[audioBytes]);
    }

    private sealed class FakeVideoAudioSource : IVideoAudioSource
    {
        public UrlVideoMetadata Metadata { get; init; } =
            CreateMetadata();

        public bool CancelDownload { get; init; }

        public bool FailDownload { get; init; }

        public int ResolveCalls { get; private set; }

        public int DownloadCalls { get; private set; }

        public Task<UrlVideoMetadata> ResolveAsync(
            Uri videoUrl,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveCalls++;
            return Task.FromResult(
                Metadata with { SourceUrl = videoUrl }
            );
        }

        public Task DownloadAudioAsync(
            UrlVideoMetadata video,
            string destinationPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default
        )
        {
            DownloadCalls++;
            Directory.CreateDirectory(
                Path.GetDirectoryName(destinationPath)!
            );
            File.WriteAllBytes(destinationPath, "partial"u8.ToArray());
            if (CancelDownload || cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    cancellationToken
                );
            }

            if (FailDownload)
            {
                throw new UrlAudioImportException(
                    UrlAudioImportFailure.Network,
                    "network failed"
                );
            }

            WritePcmWave(destinationPath);
            progress?.Report(0.5);
            progress?.Report(1);
            return Task.CompletedTask;
        }
    }

    private sealed class InlineProgress(Action<double> report)
        : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
