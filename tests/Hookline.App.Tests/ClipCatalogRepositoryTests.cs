using System.Diagnostics;
using System.IO;
using Hookline.App.Catalog;
using Microsoft.Data.Sqlite;

namespace Hookline.App.Tests;

public sealed class ClipCatalogRepositoryTests : IDisposable
{
    private readonly string _temporaryDirectory;
    private readonly string _databasePath;

    public ClipCatalogRepositoryTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-catalog-repository-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(_temporaryDirectory);
        _databasePath = Path.Combine(
            _temporaryDirectory,
            "catalog.db"
        );
    }

    [Fact]
    public void CatalogPersistsAllClipDataAcrossRepositoryInstances()
    {
        var expected = CreateEntry(
            1,
            artist: "The Artist",
            albumArt: [1, 2, 3, 4]
        );
        new ClipCatalogRepository(_databasePath).Add(expected);

        var reopened = new ClipCatalogRepository(_databasePath);
        var actual = Assert.Single(
            reopened.GetAll(CatalogSortOrder.MostRecent)
        );

        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.DisplayTitle, actual.DisplayTitle);
        Assert.Equal(expected.SourceTitle, actual.SourceTitle);
        Assert.Equal(expected.SourceArtist, actual.SourceArtist);
        Assert.Equal(expected.SourceAlbum, actual.SourceAlbum);
        Assert.Equal(expected.ExportedAt, actual.ExportedAt);
        Assert.Equal(expected.FilePath, actual.FilePath);
        Assert.Equal(expected.TrimStart, actual.TrimStart);
        Assert.Equal(expected.TrimEnd, actual.TrimEnd);
        Assert.Equal(expected.Duration, actual.Duration);
        Assert.Equal(expected.TrackInstanceId, actual.TrackInstanceId);
        Assert.Equal(expected.AlbumArt, actual.AlbumArt);
    }

    [Fact]
    public void CatalogSupportsRecentAndArtistSortOrders()
    {
        var repository = new ClipCatalogRepository(_databasePath);
        repository.AddRange(
            [
                CreateEntry(1, artist: "Zulu"),
                CreateEntry(2, artist: "alpha"),
                CreateEntry(3, artist: "Bravo"),
            ]
        );

        var recent = repository.GetAll(
            CatalogSortOrder.MostRecent
        );
        var byArtist = repository.GetAll(
            CatalogSortOrder.Artist
        );

        Assert.Equal(
            ["Clip 3", "Clip 2", "Clip 1"],
            recent.Select(entry => entry.DisplayTitle)
        );
        Assert.Equal(
            ["alpha", "Bravo", "Zulu"],
            byArtist.Select(entry => entry.SourceArtist)
        );
    }

    [Fact]
    public void TwoHundredFiftyEntriesStayWithinInteractiveLatency()
    {
        var representativeAlbumArt = new byte[32 * 1024];
        var entries = Enumerable.Range(1, 250)
            .Select(
                index =>
                    CreateEntry(
                        index,
                        artist: $"Artist {index % 25:00}",
                        albumArt: representativeAlbumArt
                    )
            )
            .ToArray();
        var repository = new ClipCatalogRepository(_databasePath);
        var stopwatch = Stopwatch.StartNew();

        repository.AddRange(entries);
        var loaded = repository.GetAll(
            CatalogSortOrder.MostRecent
        );
        stopwatch.Stop();

        Assert.Equal(250, loaded.Count);
        Assert.Equal("Clip 250", loaded[0].DisplayTitle);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Catalog write and query took {stopwatch.Elapsed}."
        );
    }

    [Fact]
    public void VersionOneCatalogMigratesAndAcceptsSyntheticIds()
    {
        using (var connection = new SqliteConnection(
            $"Data Source={_databasePath}"
        ))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE clips (
                    id TEXT NOT NULL PRIMARY KEY,
                    display_title TEXT NOT NULL,
                    source_title TEXT NOT NULL,
                    source_artist TEXT NOT NULL,
                    source_album TEXT NOT NULL,
                    exported_at_utc TEXT NOT NULL,
                    file_path TEXT NOT NULL,
                    trim_start_ticks INTEGER NOT NULL,
                    trim_end_ticks INTEGER NOT NULL,
                    duration_ticks INTEGER NOT NULL,
                    track_instance_id INTEGER NOT NULL,
                    album_art BLOB NULL,
                    CHECK (trim_start_ticks >= 0),
                    CHECK (trim_end_ticks >= trim_start_ticks),
                    CHECK (duration_ticks >= 0),
                    CHECK (track_instance_id > 0)
                );

                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }

        var repository = new ClipCatalogRepository(_databasePath);
        var liveEntry = CreateEntry(1);
        repository.Add(liveEntry);
        var importedEntry = CreateEntry(2) with
        {
            Id = Guid.NewGuid(),
            DisplayTitle = "Imported clip",
            FilePath = Path.Combine(
                _temporaryDirectory,
                "imported.mp3"
            ),
            TrackInstanceId = -1,
        };

        repository.Add(importedEntry);

        var entries = repository.GetAll(
            CatalogSortOrder.MostRecent
        );
        Assert.Contains(
            entries,
            entry =>
                entry.Id == liveEntry.Id
                && entry.TrackInstanceId == 1
        );
        Assert.Contains(
            entries,
            entry =>
                entry.Id == importedEntry.Id
                && entry.TrackInstanceId == -1
        );
        using var reopened = new SqliteConnection(
            $"Data Source={_databasePath}"
        );
        reopened.Open();
        using var version = reopened.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(2L, (long)version.ExecuteScalar()!);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_temporaryDirectory, recursive: true);
    }

    private ClipCatalogEntry CreateEntry(
        int index,
        string? artist = null,
        byte[]? albumArt = null
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            DisplayTitle = $"Clip {index}",
            SourceTitle = $"Track {index}",
            SourceArtist = artist ?? $"Artist {index}",
            SourceAlbum = $"Album {index}",
            ExportedAt = new DateTimeOffset(
                2026,
                7,
                27,
                0,
                0,
                0,
                TimeSpan.Zero
            ).AddMinutes(index),
            FilePath = Path.Combine(
                _temporaryDirectory,
                $"clip-{index}.mp3"
            ),
            TrimStart = TimeSpan.FromSeconds(index),
            TrimEnd = TimeSpan.FromSeconds(index + 3),
            Duration = TimeSpan.FromSeconds(3),
            TrackInstanceId = index,
            AlbumArt = albumArt ?? [],
        };
}
