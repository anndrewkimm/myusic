using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Hookline.Audio;

public sealed class LocalAudioFileImporter
{
    public static readonly TimeSpan DefaultMaximumDuration =
        TimeSpan.FromMinutes(30);

    public const int DefaultMaximumDecodedBytes =
        320 * 1024 * 1024;

    private const int MaximumAlbumArtBytes = 16 * 1024 * 1024;
    private const int ReadBufferBytes = 64 * 1024;

    private static readonly PcmAudioFormat TargetFormat =
        new(44_100, 16, 2);

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3",
            ".wav",
            ".m4a",
            ".aac",
            ".wma",
        };

    private readonly TimeSpan _maximumDuration;
    private readonly int _maximumDecodedBytes;
    private long _nextSyntheticTrackInstanceId;

    public LocalAudioFileImporter(
        TimeSpan? maximumDuration = null,
        int maximumDecodedBytes = DefaultMaximumDecodedBytes
    )
    {
        _maximumDuration =
            maximumDuration ?? DefaultMaximumDuration;
        if (_maximumDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDuration)
            );
        }

        if (maximumDecodedBytes < TargetFormat.BlockAlign)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDecodedBytes)
            );
        }

        _maximumDecodedBytes =
            maximumDecodedBytes
            - (maximumDecodedBytes % TargetFormat.BlockAlign);
    }

    public Task<ImportedAudioFile> ImportAsync(
        string filePath,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Task.Run(
            () => ImportCore(filePath, cancellationToken),
            cancellationToken
        );
    }

    private ImportedAudioFile ImportCore(
        string filePath,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new LocalAudioImportException(
                LocalAudioImportFailure.FileNotFound,
                AudioStrings.ImportFileMissing
            );
        }

        if (
            !SupportedExtensions.Contains(
                Path.GetExtension(fullPath)
            )
        )
        {
            throw new LocalAudioImportException(
                LocalAudioImportFailure.UnsupportedFormat,
                AudioStrings.UnsupportedImportFormat
            );
        }

        ReadOnlyMemory<byte> audio;
        try
        {
            audio = Decode(fullPath, cancellationToken);
        }
        catch (LocalAudioImportException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or COMException
                or InvalidOperationException
                or NotSupportedException
                or ArgumentException)
        {
            throw new LocalAudioImportException(
                LocalAudioImportFailure.DecodeFailed,
                AudioStrings.ImportDecodeFailed,
                exception
            );
        }

        cancellationToken.ThrowIfCancellationRequested();
        var trackInstanceId = Interlocked.Decrement(
            ref _nextSyntheticTrackInstanceId
        );
        var duration = TargetFormat.GetDuration(audio.Length);
        var metadata = ReadMetadata(fullPath);
        return new ImportedAudioFile
        {
            SourcePath = fullPath,
            Snapshot = new AudioBufferSnapshot
            {
                TrackInstanceId = trackInstanceId,
                Format = TargetFormat,
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
            Metadata = metadata,
        };
    }

    private ReadOnlyMemory<byte> Decode(
        string fullPath,
        CancellationToken cancellationToken
    )
    {
        using var reader = new MediaFoundationReader(fullPath);
        if (
            reader.TotalTime > TimeSpan.Zero
            && reader.TotalTime > _maximumDuration
        )
        {
            throw new LocalAudioImportException(
                LocalAudioImportFailure.TooLong,
                AudioStrings.ImportTooLong
            );
        }

        using var resampler = new MediaFoundationResampler(
            reader,
            new WaveFormat(
                TargetFormat.SampleRate,
                TargetFormat.BitsPerSample,
                TargetFormat.Channels
            )
        )
        {
            ResamplerQuality = 60,
        };

        using var output = new MemoryStream(
            EstimateCapacity(reader.TotalTime)
        );
        var buffer = new byte[ReadBufferBytes];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = resampler.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > _maximumDecodedBytes)
            {
                throw new LocalAudioImportException(
                    LocalAudioImportFailure.TooLarge,
                    AudioStrings.ImportTooLarge
                );
            }

            output.Write(buffer, 0, read);
        }

        var alignedLength = checked((int)output.Length);
        alignedLength -= alignedLength % TargetFormat.BlockAlign;
        if (alignedLength == 0)
        {
            throw new LocalAudioImportException(
                LocalAudioImportFailure.DecodeFailed,
                AudioStrings.ImportHasNoAudio
            );
        }

        var duration = TargetFormat.GetDuration(alignedLength);
        if (duration > _maximumDuration)
        {
            throw new LocalAudioImportException(
                LocalAudioImportFailure.TooLong,
                AudioStrings.ImportTooLong
            );
        }

        return output.GetBuffer().AsMemory(0, alignedLength);
    }

    private int EstimateCapacity(TimeSpan sourceDuration)
    {
        if (sourceDuration <= TimeSpan.Zero)
        {
            return Math.Min(1024 * 1024, _maximumDecodedBytes);
        }

        var estimate = Math.Ceiling(
            sourceDuration.TotalSeconds
            * TargetFormat.AverageBytesPerSecond
        );
        var minimumCapacity = Math.Min(
            TargetFormat.AverageBytesPerSecond,
            _maximumDecodedBytes
        );
        return (int)Math.Clamp(
            estimate,
            minimumCapacity,
            _maximumDecodedBytes
        );
    }

    private static ClipExportMetadata ReadMetadata(string fullPath)
    {
        var fallbackTitle = Path.GetFileNameWithoutExtension(
            fullPath
        );
        try
        {
            using var file = TagLib.File.Create(fullPath);
            var tag = file.Tag;
            var title = Normalize(tag.Title) ?? fallbackTitle;
            var artist = JoinArtists(tag.Performers);
            if (artist.Length == 0)
            {
                artist = JoinArtists(tag.AlbumArtists);
            }
            var album = Normalize(tag.Album) ?? string.Empty;
            var albumArt = ReadAlbumArt(tag);
            return new ClipExportMetadata
            {
                Title = title,
                Artist = artist,
                Album = album,
                AlbumArt = albumArt,
            };
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or TagLib.CorruptFileException
                or TagLib.UnsupportedFormatException)
        {
            return new ClipExportMetadata
            {
                Title = fallbackTitle,
                Artist = string.Empty,
                Album = string.Empty,
            };
        }
    }

    private static ReadOnlyMemory<byte> ReadAlbumArt(TagLib.Tag tag)
    {
        var picture =
            tag.Pictures.FirstOrDefault(
                candidate =>
                    candidate.Type == TagLib.PictureType.FrontCover
            )
            ?? tag.Pictures.FirstOrDefault();
        var bytes = picture?.Data.Data;
        return bytes is { Length: > 0 }
            && bytes.Length <= MaximumAlbumArtBytes
                ? bytes.ToArray()
                : ReadOnlyMemory<byte>.Empty;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string JoinArtists(
        IEnumerable<string> artists
    ) =>
        string.Join(
            ", ",
            artists
                .Select(Normalize)
                .Where(value => value is not null)
        );
}
