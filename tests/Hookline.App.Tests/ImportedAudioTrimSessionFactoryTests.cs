using Hookline.Audio;
using Hookline.App.Catalog;
using System.IO;

namespace Hookline.App.Tests;

public sealed class ImportedAudioTrimSessionFactoryTests
{
    [Fact]
    public void ImportedAudioBecomesANormalUnselectedTrimSession()
    {
        var format = new PcmAudioFormat(44_100, 16, 2);
        var duration = TimeSpan.FromSeconds(2);
        var snapshot = new AudioBufferSnapshot
        {
            TrackInstanceId = -42,
            Format = format,
            Audio = new byte[
                format.GetAlignedByteCount(duration)
            ],
            RequestedStart = TimeSpan.Zero,
            RequestedEnd = duration,
            AvailableStart = TimeSpan.Zero,
            AvailableEnd = duration,
            IncludedRanges =
            [
                new AudioTimeRange(TimeSpan.Zero, duration),
            ],
        };
        var artwork = new byte[] { 1, 2, 3 };
        var imported = new ImportedAudioFile
        {
            SourcePath = @"C:\Music\source.mp3",
            Snapshot = snapshot,
            Metadata = new ClipExportMetadata
            {
                Title = "Source title",
                Artist = "Source artist",
                Album = "Source album",
                AlbumArt = artwork,
            },
        };

        var session = ImportedAudioTrimSessionFactory.Create(
            imported
        );

        Assert.Same(snapshot, session.Snapshot);
        Assert.Equal(-42, session.Track.InstanceId);
        Assert.Equal("Source title", session.Track.Title);
        Assert.Equal("Source artist", session.Track.Artist);
        Assert.Equal("Source album", session.Track.Album);
        Assert.Equal(duration, session.Track.Duration);
        Assert.Equal(artwork, session.Track.AlbumArt.ToArray());
        Assert.Null(session.InitialSelectionStart);
        Assert.Null(session.InitialSelectionEnd);
    }

    [Fact]
    public async Task ImportedSessionExportsAndRegistersNormally()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-import-pipeline-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var format = new PcmAudioFormat(44_100, 16, 2);
            var duration = TimeSpan.FromSeconds(2);
            var snapshot = new AudioBufferSnapshot
            {
                TrackInstanceId = -7,
                Format = format,
                Audio = new byte[
                    format.GetAlignedByteCount(duration)
                ],
                RequestedStart = TimeSpan.Zero,
                RequestedEnd = duration,
                AvailableStart = TimeSpan.Zero,
                AvailableEnd = duration,
                IncludedRanges =
                [
                    new AudioTimeRange(
                        TimeSpan.Zero,
                        duration
                    ),
                ],
            };
            var metadata = new ClipExportMetadata
            {
                Title = "Imported source",
                Artist = "Local artist",
                Album = "Local album",
            };
            var imported = new ImportedAudioFile
            {
                SourcePath = Path.Combine(
                    temporaryDirectory,
                    "source.wav"
                ),
                Snapshot = snapshot,
                Metadata = metadata,
            };
            var session =
                ImportedAudioTrimSessionFactory.Create(imported);
            var selection = AudioSnapshotSlicer.Slice(
                session.Snapshot,
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(1.25)
            );
            var repository = new ClipCatalogRepository(
                Path.Combine(temporaryDirectory, "catalog.db")
            );
            var catalog = new ClipCatalogService(repository);
            var exporter = new CatalogingClipExporter(
                new Mp3ClipExporter(),
                catalog
            );

            var result = await exporter.ExportAsync(
                selection,
                metadata,
                temporaryDirectory
            );

            Assert.True(File.Exists(result.OutputPath));
            using (var tagged = TagLib.File.Create(result.OutputPath))
            {
                Assert.Equal(metadata.Title, tagged.Tag.Title);
                Assert.Equal(
                    metadata.Artist,
                    Assert.Single(tagged.Tag.Performers)
                );
                Assert.Equal(metadata.Album, tagged.Tag.Album);
            }

            var catalogEntry = Assert.Single(
                repository.GetAll(CatalogSortOrder.MostRecent)
            );
            Assert.Equal(-7, catalogEntry.TrackInstanceId);
            Assert.Equal(metadata.Title, catalogEntry.SourceTitle);
            Assert.Equal(
                TimeSpan.FromMilliseconds(250),
                catalogEntry.TrimStart
            );
            Assert.Equal(
                TimeSpan.FromSeconds(1.25),
                catalogEntry.TrimEnd
            );
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(
                temporaryDirectory,
                recursive: true
            );
        }
    }
}
