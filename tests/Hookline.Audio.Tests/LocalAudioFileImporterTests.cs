using System.Runtime.InteropServices;
using System.Text;

namespace Hookline.Audio.Tests;

public sealed class LocalAudioFileImporterTests : IDisposable
{
    private readonly string _temporaryDirectory;

    public LocalAudioFileImporterTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-import-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public async Task WavIsNormalizedIntoAContinuousSnapshot()
    {
        var sourcePath = Path.Combine(
            _temporaryDirectory,
            "mono-source.wav"
        );
        WritePcmWave(
            sourcePath,
            sampleRate: 22_050,
            channels: 1,
            TimeSpan.FromMilliseconds(250)
        );

        var imported = await new LocalAudioFileImporter()
            .ImportAsync(sourcePath);

        Assert.Equal(Path.GetFullPath(sourcePath), imported.SourcePath);
        Assert.True(imported.Snapshot.TrackInstanceId < 0);
        Assert.Equal(44_100, imported.Snapshot.Format.SampleRate);
        Assert.Equal(16, imported.Snapshot.Format.BitsPerSample);
        Assert.Equal(2, imported.Snapshot.Format.Channels);
        Assert.Equal(
            TimeSpan.Zero,
            imported.Snapshot.RequestedStart
        );
        Assert.Equal(
            imported.Snapshot.Duration,
            imported.Snapshot.RequestedEnd
        );
        Assert.Equal(
            TimeSpan.Zero,
            imported.Snapshot.AvailableStart
        );
        Assert.Equal(
            imported.Snapshot.Duration,
            imported.Snapshot.AvailableEnd
        );
        var range = Assert.Single(
            imported.Snapshot.IncludedRanges
        );
        Assert.Equal(TimeSpan.Zero, range.Start);
        Assert.Equal(imported.Snapshot.Duration, range.End);
        Assert.Empty(imported.Snapshot.ExcludedRanges);
        Assert.False(imported.Snapshot.HasGaps);
        Assert.InRange(
            imported.Snapshot.Duration.TotalMilliseconds,
            240,
            260
        );
        Assert.Equal("mono-source", imported.Metadata.Title);
        Assert.Equal(string.Empty, imported.Metadata.Artist);
        Assert.Equal(string.Empty, imported.Metadata.Album);
        Assert.True(imported.Metadata.AlbumArt.IsEmpty);
    }

    [Fact]
    public async Task Mp3TagsAndArtworkArePreservedWithUniqueIds()
    {
        var albumArt = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
        );
        var metadata = new ClipExportMetadata
        {
            Title = "Imported title",
            Artist = "Imported artist",
            Album = "Imported album",
            AlbumArt = albumArt,
        };
        var source = CreateSnapshot(TimeSpan.FromSeconds(1));
        var exported = await new Mp3ClipExporter().ExportAsync(
            source,
            metadata,
            _temporaryDirectory
        );
        var importer = new LocalAudioFileImporter();

        var first = await importer.ImportAsync(exported.OutputPath);
        var second = await importer.ImportAsync(exported.OutputPath);

        Assert.Equal(metadata.Title, first.Metadata.Title);
        Assert.Equal(metadata.Artist, first.Metadata.Artist);
        Assert.Equal(metadata.Album, first.Metadata.Album);
        Assert.Equal(
            albumArt,
            first.Metadata.AlbumArt.ToArray()
        );
        Assert.True(first.Snapshot.TrackInstanceId < 0);
        Assert.True(second.Snapshot.TrackInstanceId < 0);
        Assert.NotEqual(
            first.Snapshot.TrackInstanceId,
            second.Snapshot.TrackInstanceId
        );
        Assert.InRange(
            first.Snapshot.Duration.TotalSeconds,
            0.8,
            1.2
        );
    }

    [Fact]
    public async Task UnsupportedAndCorruptFilesFailSpecifically()
    {
        var unsupportedPath = Path.Combine(
            _temporaryDirectory,
            "notes.txt"
        );
        await File.WriteAllTextAsync(
            unsupportedPath,
            "not audio"
        );
        var corruptPath = Path.Combine(
            _temporaryDirectory,
            "corrupt.mp3"
        );
        await File.WriteAllBytesAsync(
            corruptPath,
            "not an mp3"u8.ToArray()
        );
        var importer = new LocalAudioFileImporter();

        var unsupported = await Assert.ThrowsAsync<
            LocalAudioImportException
        >(() => importer.ImportAsync(unsupportedPath));
        var corrupt = await Assert.ThrowsAsync<
            LocalAudioImportException
        >(() => importer.ImportAsync(corruptPath));

        Assert.Equal(
            LocalAudioImportFailure.UnsupportedFormat,
            unsupported.Failure
        );
        Assert.Equal(
            LocalAudioImportFailure.DecodeFailed,
            corrupt.Failure
        );
    }

    [Theory]
    [InlineData(".mp4")]
    [InlineData(".mkv")]
    [InlineData(".webm")]
    public async Task VideoContainerExtensionsReachTheDecoder(
        string extension
    )
    {
        var sourcePath = Path.Combine(
            _temporaryDirectory,
            $"corrupt-video{extension}"
        );
        await File.WriteAllBytesAsync(
            sourcePath,
            "not media"u8.ToArray()
        );

        var exception = await Assert.ThrowsAsync<
            LocalAudioImportException
        >(
            () =>
                new LocalAudioFileImporter()
                    .ImportAsync(sourcePath)
        );

        Assert.Equal(
            LocalAudioImportFailure.DecodeFailed,
            exception.Failure
        );
        Assert.Equal(
            AudioStrings.ImportDecodeFailed,
            exception.Message
        );
    }

    [Fact]
    public void MissingAudioStreamGetsTheSpecificNoAudioMessage()
    {
        var mediaFoundationFailure = new COMException(
            "The stream number provided was invalid.",
            unchecked((int)0xC00D36B3)
        );

        var exception =
            LocalAudioImportErrorMapper.MapDecodeFailure(
                mediaFoundationFailure
            );

        Assert.Equal(
            LocalAudioImportFailure.DecodeFailed,
            exception.Failure
        );
        Assert.Equal(
            AudioStrings.ImportHasNoAudio,
            exception.Message
        );
        Assert.Same(
            mediaFoundationFailure,
            exception.InnerException
        );
    }

    [Fact]
    public void MissingContainerArtistTagsUseTheExistingFallback()
    {
        Assert.Equal(
            string.Empty,
            LocalAudioFileImporter.JoinArtists(null)
        );
    }

    [Fact]
    public async Task DurationAndDecodedSizeCapsFailSpecifically()
    {
        var sourcePath = Path.Combine(
            _temporaryDirectory,
            "bounded.wav"
        );
        WritePcmWave(
            sourcePath,
            sampleRate: 44_100,
            channels: 2,
            TimeSpan.FromMilliseconds(500)
        );
        var durationLimited = new LocalAudioFileImporter(
            maximumDuration: TimeSpan.FromMilliseconds(100)
        );
        var sizeLimited = new LocalAudioFileImporter(
            maximumDecodedBytes: 4_096
        );

        var tooLong = await Assert.ThrowsAsync<
            LocalAudioImportException
        >(() => durationLimited.ImportAsync(sourcePath));
        var tooLarge = await Assert.ThrowsAsync<
            LocalAudioImportException
        >(() => sizeLimited.ImportAsync(sourcePath));

        Assert.Equal(
            LocalAudioImportFailure.TooLong,
            tooLong.Failure
        );
        Assert.Equal(
            LocalAudioImportFailure.TooLarge,
            tooLarge.Failure
        );
    }

    public void Dispose() =>
        Directory.Delete(_temporaryDirectory, recursive: true);

    private static AudioBufferSnapshot CreateSnapshot(
        TimeSpan duration
    )
    {
        var format = new PcmAudioFormat(44_100, 16, 2);
        var audio = CreateTone(format, duration);
        return new AudioBufferSnapshot
        {
            TrackInstanceId = 1,
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
        };
    }

    private static byte[] CreateTone(
        PcmAudioFormat format,
        TimeSpan duration
    )
    {
        var audio = new byte[
            format.GetAlignedByteCount(duration)
        ];
        var frames = audio.Length / format.BlockAlign;
        for (var frame = 0; frame < frames; frame++)
        {
            var sample = (short)(
                Math.Sin(
                    2
                    * Math.PI
                    * 440
                    * frame
                    / format.SampleRate
                )
                * short.MaxValue
                * 0.2
            );
            for (
                var channel = 0;
                channel < format.Channels;
                channel++
            )
            {
                var offset =
                    (frame * format.BlockAlign)
                    + (channel * sizeof(short));
                BitConverter.TryWriteBytes(
                    audio.AsSpan(offset, sizeof(short)),
                    sample
                );
            }
        }

        return audio;
    }

    private static void WritePcmWave(
        string path,
        int sampleRate,
        short channels,
        TimeSpan duration
    )
    {
        var format = new PcmAudioFormat(
            sampleRate,
            16,
            channels
        );
        var audio = CreateTone(format, duration);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(
            stream,
            Encoding.ASCII,
            leaveOpen: false
        );
        writer.Write("RIFF"u8);
        writer.Write(36 + audio.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(format.AverageBytesPerSecond);
        writer.Write((short)format.BlockAlign);
        writer.Write((short)format.BitsPerSample);
        writer.Write("data"u8);
        writer.Write(audio.Length);
        writer.Write(audio);
    }
}
