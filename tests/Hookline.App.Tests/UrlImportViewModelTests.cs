using Hookline.Audio;

namespace Hookline.App.Tests;

public sealed class UrlImportViewModelTests
{
    [Fact]
    public async Task ResolveShowsPreviewBeforeImport()
    {
        var service = new FakeImportService();
        using var viewModel = new UrlImportViewModel(
            service,
            showPersonalUseNotice: true
        )
        {
            Url = "https://youtu.be/dQw4w9WgXcQ",
        };

        Assert.True(viewModel.CanFetch);
        Assert.False(viewModel.CanImport);
        Assert.True(viewModel.IsNoticeVisible);

        await viewModel.ResolveAsync();

        Assert.Equal(1, service.ResolveCalls);
        Assert.Equal(0, service.ImportCalls);
        Assert.True(viewModel.HasPreview);
        Assert.True(viewModel.CanImport);
        Assert.Equal("Video title", viewModel.Title);
        Assert.Equal("Channel", viewModel.Channel);
        Assert.Equal("Duration: 3:05", viewModel.DurationText);
        Assert.Equal("thumbnail"u8.ToArray(), viewModel.Thumbnail);
        Assert.Equal(AppStrings.UrlImportReady, viewModel.StatusMessage);
    }

    [Fact]
    public async Task ConfirmReturnsImportedAudio()
    {
        var service = new FakeImportService();
        using var viewModel = new UrlImportViewModel(
            service,
            showPersonalUseNotice: false
        )
        {
            Url = "https://youtu.be/dQw4w9WgXcQ",
        };
        await viewModel.ResolveAsync();

        var imported = await viewModel.ImportAsync();

        Assert.Same(service.Imported, imported);
        Assert.Equal(1, service.ImportCalls);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task CancellationReturnsDialogToInitialState()
    {
        var service = new FakeImportService
        {
            WaitForCancellation = true,
        };
        using var viewModel = new UrlImportViewModel(
            service,
            showPersonalUseNotice: false
        )
        {
            Url = "https://youtu.be/dQw4w9WgXcQ",
        };

        var resolving = viewModel.ResolveAsync();
        await service.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(2)
        );
        viewModel.Cancel();
        await resolving;

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasPreview);
        Assert.False(viewModel.CanImport);
        Assert.Equal(
            AppStrings.UrlImportCanceled,
            viewModel.StatusMessage
        );
    }

    [Fact]
    public async Task DownloadCancellationClearsConfirmedPreview()
    {
        var service = new FakeImportService
        {
            WaitForImportCancellation = true,
        };
        using var viewModel = new UrlImportViewModel(
            service,
            showPersonalUseNotice: false
        )
        {
            Url = "https://youtu.be/dQw4w9WgXcQ",
        };
        await viewModel.ResolveAsync();

        var importing = viewModel.ImportAsync();
        await service.ImportStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2)
        );
        viewModel.Cancel();
        var imported = await importing;

        Assert.Null(imported);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasPreview);
        Assert.Equal(
            AppStrings.UrlImportCanceled,
            viewModel.StatusMessage
        );
    }

    [Fact]
    public async Task FailureBecomesAStatusInsteadOfEscaping()
    {
        var service = new FakeImportService
        {
            Failure = new UrlAudioImportException(
                UrlAudioImportFailure.VideoUnavailable,
                "video unavailable"
            ),
        };
        using var viewModel = new UrlImportViewModel(
            service,
            showPersonalUseNotice: false
        )
        {
            Url = "https://youtu.be/dQw4w9WgXcQ",
        };

        await viewModel.ResolveAsync();

        Assert.False(viewModel.HasPreview);
        Assert.Equal("video unavailable", viewModel.StatusMessage);
    }

    private sealed class FakeImportService : IUrlAudioImportService
    {
        public Exception? Failure { get; init; }

        public bool WaitForCancellation { get; init; }

        public bool WaitForImportCancellation { get; init; }

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public TaskCompletionSource ImportStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public int ResolveCalls { get; private set; }

        public int ImportCalls { get; private set; }

        public ImportedAudioFile Imported { get; } = new()
        {
            SourcePath = "temporary.m4a",
            Snapshot = new AudioBufferSnapshot
            {
                TrackInstanceId = -1,
                Format = new PcmAudioFormat(44_100, 16, 2),
                Audio = new byte[4],
                RequestedStart = TimeSpan.Zero,
                RequestedEnd = TimeSpan.FromTicks(1),
                AvailableStart = TimeSpan.Zero,
                AvailableEnd = TimeSpan.FromTicks(1),
                IncludedRanges =
                [
                    new AudioTimeRange(
                        TimeSpan.Zero,
                        TimeSpan.FromTicks(1)
                    ),
                ],
            },
            Metadata = new ClipExportMetadata
            {
                Title = "Video title",
                Artist = "Channel",
                Album = string.Empty,
            },
        };

        public async Task<UrlVideoMetadata> ResolveAsync(
            string url,
            CancellationToken cancellationToken = default
        )
        {
            ResolveCalls++;
            Started.TrySetResult();
            if (WaitForCancellation)
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken
                );
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            return new UrlVideoMetadata
            {
                SourceUrl = new Uri(url),
                VideoId = "dQw4w9WgXcQ",
                Title = "Video title",
                Channel = "Channel",
                Duration = TimeSpan.FromMinutes(3)
                    + TimeSpan.FromSeconds(5),
                AudioFileExtension = ".m4a",
                Thumbnail = "thumbnail"u8.ToArray(),
            };
        }

        public async Task<ImportedAudioFile> ImportAsync(
            UrlVideoMetadata video,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportCalls++;
            ImportStarted.TrySetResult();
            if (WaitForImportCancellation)
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken
                );
            }

            progress?.Report(1);
            return Imported;
        }
    }
}
