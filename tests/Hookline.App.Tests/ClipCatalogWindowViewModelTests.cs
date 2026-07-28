using System.IO;
using Hookline.App.Catalog;

namespace Hookline.App.Tests;

public sealed class ClipCatalogWindowViewModelTests : IDisposable
{
    private readonly string _temporaryDirectory;
    private readonly string _databasePath;

    public ClipCatalogWindowViewModelTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-catalog-viewmodel-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(_temporaryDirectory);
        _databasePath = Path.Combine(
            _temporaryDirectory,
            "catalog.db"
        );
    }

    [Fact]
    public async Task FileActionsUseTheSelectedClipAndDeleteStopsPlaybackFirst()
    {
        var filePath = Path.Combine(
            _temporaryDirectory,
            "action-clip.mp3"
        );
        File.WriteAllText(filePath, "clip");
        var repository = new ClipCatalogRepository(_databasePath);
        var entry = CreateEntry(filePath);
        repository.Add(entry);
        var catalog = new ClipCatalogService(
            repository,
            tagEditor: new NoOpTagEditor()
        );
        var player = new RecordingPlayer();
        var retrim = new RecordingRetrimLauncher();
        var reveal = new RecordingRevealService();
        using var viewModel = new ClipCatalogWindowViewModel(
            catalog,
            player,
            retrim,
            reveal
        );
        await viewModel.LoadAsync();
        var item = Assert.Single(viewModel.Items);

        await viewModel.TogglePlaybackAsync(item);
        await viewModel.RetrimAsync(item);
        await viewModel.RevealAsync(item);
        await viewModel.DeleteAsync(item);

        Assert.Equal(entry.Id, player.LastPlayedClipId);
        Assert.Equal(filePath, player.LastPlayedPath);
        Assert.Equal(entry.Id, retrim.LastEntry?.Id);
        Assert.Equal(filePath, reveal.LastRevealedPath);
        Assert.True(player.StopObservedFileStillPresent);
        Assert.False(File.Exists(filePath));
        Assert.Null(repository.GetById(entry.Id));
        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public async Task RetrimExplainsWhenTheRollingBufferExpired()
    {
        var filePath = Path.Combine(
            _temporaryDirectory,
            "expired-clip.mp3"
        );
        File.WriteAllText(filePath, "clip");
        var repository = new ClipCatalogRepository(_databasePath);
        repository.Add(CreateEntry(filePath));
        var catalog = new ClipCatalogService(
            repository,
            tagEditor: new NoOpTagEditor()
        );
        var retrim = new RecordingRetrimLauncher
        {
            Result = ClipRetrimResult.BufferUnavailable,
        };
        using var viewModel = new ClipCatalogWindowViewModel(
            catalog,
            new RecordingPlayer(),
            retrim,
            new RecordingRevealService()
        );
        await viewModel.LoadAsync();

        await viewModel.RetrimAsync(
            Assert.Single(viewModel.Items)
        );

        Assert.Equal(
            AppStrings.CatalogRetrimUnavailable,
            viewModel.StatusMessage
        );
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_temporaryDirectory, recursive: true);
    }

    private static ClipCatalogEntry CreateEntry(string filePath) =>
        new()
        {
            Id = Guid.NewGuid(),
            DisplayTitle = "Clip",
            SourceTitle = "Track",
            SourceArtist = "Artist",
            SourceAlbum = "Album",
            ExportedAt = DateTimeOffset.UtcNow,
            FilePath = filePath,
            TrimStart = TimeSpan.FromSeconds(1),
            TrimEnd = TimeSpan.FromSeconds(3),
            Duration = TimeSpan.FromSeconds(2),
            TrackInstanceId = 44,
        };

    private sealed class NoOpTagEditor : IClipTagEditor
    {
        public void UpdateTitle(string filePath, string title)
        {
        }
    }

    private sealed class RecordingPlayer : IClipPlaybackPlayer
    {
        public event EventHandler<ClipPlaybackChangedEventArgs>?
            PlaybackChanged;

        public Guid? CurrentClipId { get; private set; }

        public bool IsPlaying { get; private set; }

        public Guid? LastPlayedClipId { get; private set; }

        public string? LastPlayedPath { get; private set; }

        public bool StopObservedFileStillPresent { get; private set; }

        public void Play(Guid clipId, string filePath)
        {
            CurrentClipId = clipId;
            IsPlaying = true;
            LastPlayedClipId = clipId;
            LastPlayedPath = filePath;
            PlaybackChanged?.Invoke(
                this,
                new ClipPlaybackChangedEventArgs(
                    clipId,
                    isPlaying: true
                )
            );
        }

        public void Stop()
        {
            StopObservedFileStillPresent =
                LastPlayedPath is not null
                && File.Exists(LastPlayedPath);
            var clipId = CurrentClipId;
            CurrentClipId = null;
            IsPlaying = false;
            PlaybackChanged?.Invoke(
                this,
                new ClipPlaybackChangedEventArgs(
                    clipId,
                    isPlaying: false
                )
            );
        }

        public void Dispose()
        {
            CurrentClipId = null;
            IsPlaying = false;
        }
    }

    private sealed class RecordingRetrimLauncher
        : IClipRetrimLauncher
    {
        public ClipRetrimResult Result { get; init; } =
            ClipRetrimResult.Opened;

        public ClipCatalogEntry? LastEntry { get; private set; }

        public Task<ClipRetrimResult> OpenAsync(
            ClipCatalogEntry entry,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastEntry = entry;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingRevealService
        : IClipRevealService
    {
        public string? LastRevealedPath { get; private set; }

        public void Reveal(string filePath) =>
            LastRevealedPath = filePath;
    }
}
