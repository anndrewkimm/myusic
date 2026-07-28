namespace Hookline.Audio.Tests;

public sealed class Mp3ClipExporterTests
{
    [Fact]
    public async Task VeryShortSelectionCreatesMissingDefaultFolder()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-short-export-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var outputDirectory = Path.Combine(
                temporaryDirectory,
                "Missing",
                "Hookline"
            );
            var format = new PcmAudioFormat(44_100, 16, 2);
            var duration = TimeSpan.FromMilliseconds(50);
            var snapshot = new AudioBufferSnapshot
            {
                TrackInstanceId = 8,
                Format = format,
                Audio = CreateTone(format, duration),
                RequestedStart = TimeSpan.Zero,
                RequestedEnd = duration,
                AvailableStart = TimeSpan.Zero,
                AvailableEnd = duration,
                IncludedRanges =
                [
                    new AudioTimeRange(TimeSpan.Zero, duration),
                ],
            };

            var result = await new Mp3ClipExporter().ExportAsync(
                snapshot,
                new ClipExportMetadata
                {
                    Title = string.Empty,
                    Artist = string.Empty,
                    Album = string.Empty,
                },
                outputDirectory
            );

            Assert.True(Directory.Exists(outputDirectory));
            Assert.Equal(
                "Untitled clip.mp3",
                Path.GetFileName(result.OutputPath)
            );
            Assert.True(new FileInfo(result.OutputPath).Length > 0);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportWritesTagsAndNeverOverwritesACollision()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-export-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var exporter = new Mp3ClipExporter();
            var format = new PcmAudioFormat(44_100, 16, 2);
            var audio = CreateTone(format, TimeSpan.FromSeconds(1));
            var snapshot = new AudioBufferSnapshot
            {
                TrackInstanceId = 7,
                Format = format,
                Audio = audio,
                RequestedStart = TimeSpan.Zero,
                RequestedEnd = TimeSpan.FromSeconds(1),
                AvailableStart = TimeSpan.Zero,
                AvailableEnd = TimeSpan.FromSeconds(1),
                IncludedRanges =
                [
                    new AudioTimeRange(
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(1)
                    ),
                ],
            };
            var metadata = new ClipExportMetadata
            {
                Title = "One:Two",
                Artist = "AC/DC",
                Album = "Test Album",
                AlbumArt = Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
                ),
            };

            var first = await exporter.ExportAsync(
                snapshot,
                metadata,
                temporaryDirectory
            );
            var second = await exporter.ExportAsync(
                snapshot,
                metadata,
                temporaryDirectory
            );

            Assert.Equal(
                "AC-DC - One-Two.mp3",
                Path.GetFileName(first.OutputPath)
            );
            Assert.Equal(
                "AC-DC - One-Two (2).mp3",
                Path.GetFileName(second.OutputPath)
            );
            Assert.True(File.Exists(first.OutputPath));
            Assert.True(File.Exists(second.OutputPath));
            Assert.True(new FileInfo(first.OutputPath).Length > 0);

            using (var taggedFile = TagLib.File.Create(first.OutputPath))
            {
                Assert.Equal(metadata.Title, taggedFile.Tag.Title);
                Assert.Equal(
                    metadata.Artist,
                    Assert.Single(taggedFile.Tag.Performers)
                );
                Assert.Equal(metadata.Album, taggedFile.Tag.Album);
                Assert.Single(taggedFile.Tag.Pictures);
                Assert.True(
                    taggedFile.Properties.Duration
                        > TimeSpan.FromMilliseconds(800)
                );
            }

            new Mp3TagEditor().UpdateTitle(
                first.OutputPath,
                "Renamed clip"
            );
            using var renamedFile = TagLib.File.Create(
                first.OutputPath
            );
            Assert.Equal("Renamed clip", renamedFile.Tag.Title);
            Assert.Equal(
                metadata.Artist,
                Assert.Single(renamedFile.Tag.Performers)
            );
            Assert.Equal(metadata.Album, renamedFile.Tag.Album);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static byte[] CreateTone(
        PcmAudioFormat format,
        TimeSpan duration
    )
    {
        var frames = (int)(
            duration.TotalSeconds * format.SampleRate
        );
        var audio = new byte[frames * format.BlockAlign];
        for (var frame = 0; frame < frames; frame++)
        {
            var value = (short)(
                Math.Sin(
                    2
                    * Math.PI
                    * 440
                    * frame
                    / format.SampleRate
                )
                * 8_000
            );
            for (
                var channel = 0;
                channel < format.Channels;
                channel++
            )
            {
                BitConverter.TryWriteBytes(
                    audio.AsSpan(
                        (frame * format.BlockAlign)
                            + (channel * sizeof(short)),
                        sizeof(short)
                    ),
                    value
                );
            }
        }

        return audio;
    }
}
