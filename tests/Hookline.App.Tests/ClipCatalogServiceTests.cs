using System.IO;
using Hookline.App.Catalog;
using Hookline.Audio;

namespace Hookline.App.Tests;

public sealed class ClipCatalogServiceTests : IDisposable
{
    private readonly string _temporaryDirectory;
    private readonly string _databasePath;

    public ClipCatalogServiceTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-catalog-service-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(_temporaryDirectory);
        _databasePath = Path.Combine(
            _temporaryDirectory,
            "catalog.db"
        );
    }

    [Fact]
    public async Task SuccessfulExportIsRegisteredAutomatically()
    {
        var repository = new ClipCatalogRepository(_databasePath);
        var service = new ClipCatalogService(
            repository,
            tagEditor: new RecordingTagEditor()
        );
        var outputPath = Path.Combine(
            _temporaryDirectory,
            "new-clip.mp3"
        );
        var exporter = new CatalogingClipExporter(
            new FileCreatingExporter(outputPath),
            service
        );
        service.Changed += (_, _) =>
            throw new InvalidOperationException(
                "A broken observer must not undo the export."
            );
        var selection = CreateSelection();
        var metadata = new ClipExportMetadata
        {
            Title = "Saved moment",
            Artist = "Artist",
            Album = "Album",
            AlbumArt = new byte[] { 9, 8, 7 },
        };

        var result = await exporter.ExportAsync(
            selection,
            metadata,
            _temporaryDirectory
        );

        Assert.Equal(outputPath, result.OutputPath);
        Assert.True(File.Exists(outputPath));
        var entry = Assert.Single(
            repository.GetAll(CatalogSortOrder.MostRecent)
        );
        Assert.Equal(metadata.Title, entry.DisplayTitle);
        Assert.Equal(metadata.Artist, entry.SourceArtist);
        Assert.Equal(selection.RequestedStart, entry.TrimStart);
        Assert.Equal(selection.RequestedEnd, entry.TrimEnd);
        Assert.Equal(selection.TrackInstanceId, entry.TrackInstanceId);
        Assert.Equal(metadata.AlbumArt.ToArray(), entry.AlbumArt);
    }

    [Fact]
    public async Task LoadDetectsFilesRemovedOutsideHookline()
    {
        var existingPath = Path.Combine(
            _temporaryDirectory,
            "existing.mp3"
        );
        File.WriteAllText(existingPath, "present");
        var repository = new ClipCatalogRepository(_databasePath);
        var existing = CreateEntry(existingPath, "Existing");
        var missing = CreateEntry(
            Path.Combine(_temporaryDirectory, "missing.mp3"),
            "Missing"
        );
        repository.AddRange([existing, missing]);
        var service = new ClipCatalogService(
            repository,
            tagEditor: new RecordingTagEditor()
        );

        var entries = await service.GetAllAsync(
            CatalogSortOrder.MostRecent
        );

        Assert.False(
            Assert.Single(
                entries,
                entry => entry.Id == existing.Id
            ).IsMissing
        );
        Assert.True(
            Assert.Single(
                entries,
                entry => entry.Id == missing.Id
            ).IsMissing
        );
    }

    [Fact]
    public async Task RenameUpdatesTagAndCatalogWithoutMovingFile()
    {
        var filePath = Path.Combine(
            _temporaryDirectory,
            "stable-name.mp3"
        );
        File.WriteAllText(filePath, "clip");
        var repository = new ClipCatalogRepository(_databasePath);
        var entry = CreateEntry(filePath, "Old title");
        repository.Add(entry);
        var tagEditor = new RecordingTagEditor();
        var service = new ClipCatalogService(
            repository,
            tagEditor: tagEditor
        );

        await service.RenameAsync(entry.Id, "  New title  ");

        var renamed = repository.GetById(entry.Id);
        Assert.NotNull(renamed);
        Assert.Equal("New title", renamed.DisplayTitle);
        Assert.Equal(filePath, renamed.FilePath);
        Assert.Equal(
            [(filePath, "New title")],
            tagEditor.Updates
        );
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task DeleteRemovesCatalogEntryAndUnderlyingFile()
    {
        var filePath = Path.Combine(
            _temporaryDirectory,
            "delete-me.mp3"
        );
        File.WriteAllText(filePath, "clip");
        var repository = new ClipCatalogRepository(_databasePath);
        var entry = CreateEntry(filePath, "Delete me");
        repository.Add(entry);
        var service = new ClipCatalogService(
            repository,
            tagEditor: new RecordingTagEditor()
        );

        await service.DeleteAsync(entry.Id);

        Assert.False(File.Exists(filePath));
        Assert.Null(repository.GetById(entry.Id));
    }

    [Fact]
    public async Task DeleteAlsoRemovesAStaleMissingEntry()
    {
        var repository = new ClipCatalogRepository(_databasePath);
        var entry = CreateEntry(
            Path.Combine(_temporaryDirectory, "gone.mp3"),
            "Gone"
        );
        repository.Add(entry);
        var service = new ClipCatalogService(
            repository,
            tagEditor: new RecordingTagEditor()
        );

        await service.DeleteAsync(entry.Id);

        Assert.Null(repository.GetById(entry.Id));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_temporaryDirectory, recursive: true);
    }

    private static AudioBufferSnapshot CreateSelection()
    {
        var format = new PcmAudioFormat(100, 16, 1);
        return new AudioBufferSnapshot
        {
            TrackInstanceId = 72,
            Format = format,
            Audio = new byte[600],
            RequestedStart = TimeSpan.FromSeconds(12),
            RequestedEnd = TimeSpan.FromSeconds(15),
            AvailableStart = TimeSpan.FromSeconds(12),
            AvailableEnd = TimeSpan.FromSeconds(15),
            IncludedRanges =
            [
                new AudioTimeRange(
                    TimeSpan.FromSeconds(12),
                    TimeSpan.FromSeconds(15)
                ),
            ],
        };
    }

    private static ClipCatalogEntry CreateEntry(
        string filePath,
        string title
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            DisplayTitle = title,
            SourceTitle = "Track",
            SourceArtist = "Artist",
            SourceAlbum = "Album",
            ExportedAt = DateTimeOffset.UtcNow,
            FilePath = filePath,
            TrimStart = TimeSpan.FromSeconds(2),
            TrimEnd = TimeSpan.FromSeconds(5),
            Duration = TimeSpan.FromSeconds(3),
            TrackInstanceId = 72,
        };

    private sealed class RecordingTagEditor : IClipTagEditor
    {
        public List<(string Path, string Title)> Updates { get; } =
            [];

        public void UpdateTitle(string filePath, string title) =>
            Updates.Add((filePath, title));
    }

    private sealed class FileCreatingExporter : IClipExporter
    {
        private readonly string _outputPath;

        public FileCreatingExporter(string outputPath)
        {
            _outputPath = outputPath;
        }

        public Task<ClipExportResult> ExportAsync(
            AudioBufferSnapshot selection,
            ClipExportMetadata metadata,
            string outputFolder,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(_outputPath, "mp3");
            return Task.FromResult(
                new ClipExportResult
                {
                    OutputPath = _outputPath,
                    Duration = selection.Duration,
                }
            );
        }
    }
}
