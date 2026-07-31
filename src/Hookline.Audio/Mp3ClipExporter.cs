using System.Text;
using NAudio.Lame;
using NAudio.Wave;

namespace Hookline.Audio;

public sealed class Mp3ClipExporter : IClipExporter
{
    private const int BitRate = 192;
    private static readonly char[] InvalidFileNameCharacters =
        Path.GetInvalidFileNameChars();
    private static readonly HashSet<string> ReservedFileNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9",
        };

    public Task<ClipExportResult> ExportAsync(
        AudioBufferSnapshot selection,
        ClipExportMetadata metadata,
        string outputFolder,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFolder);

        return Task.Run(
            () =>
                ExportCore(
                    selection,
                    metadata,
                    outputFolder,
                    cancellationToken
                ),
            cancellationToken
        );
    }

    private static ClipExportResult ExportCore(
        AudioBufferSnapshot selection,
        ClipExportMetadata metadata,
        string outputFolder,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (selection.Audio.IsEmpty)
        {
            throw new InvalidOperationException(
                AudioStrings.EmptyExport
            );
        }

        if (selection.Format.BitsPerSample != 16)
        {
            throw new NotSupportedException(
                AudioStrings.UnsupportedExportFormat
            );
        }

        Directory.CreateDirectory(outputFolder);
        var fileStem = BuildFileStem(metadata);
        var temporaryPath = Path.Combine(
            outputFolder,
            $".hookline-{Guid.NewGuid():N}.mp3"
        );

        try
        {
            var fadedAudio = ApplyEdgeFades(
                selection.Audio.Span,
                selection.Format
            );
            Encode(
                temporaryPath,
                fadedAudio,
                selection.Format,
                cancellationToken
            );
            WriteTags(temporaryPath, metadata);
            cancellationToken.ThrowIfCancellationRequested();

            var outputPath = MoveToAvailableName(
                temporaryPath,
                outputFolder,
                fileStem,
                cancellationToken
            );
            return new ClipExportResult
            {
                OutputPath = outputPath,
                Duration = selection.Format.GetDuration(
                    fadedAudio.Length
                ),
            };
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    internal static byte[] ApplyEdgeFades(
        ReadOnlySpan<byte> source,
        PcmAudioFormat format
    )
    {
        var audio = source.ToArray();
        var frameCount = audio.Length / format.BlockAlign;
        var desiredFadeFrames = (int)Math.Round(
            ClipFadeSettings.Duration.TotalSeconds
            * format.SampleRate
        );
        var fadeFrames = Math.Min(
            desiredFadeFrames,
            frameCount / 2
        );
        if (fadeFrames <= 0)
        {
            return audio;
        }

        for (var frame = 0; frame < fadeFrames; frame++)
        {
            var fadeIn = frame / (double)fadeFrames;
            var fadeOut = (fadeFrames - frame - 1)
                / (double)fadeFrames;
            ScaleFrame(audio, frame, format, fadeIn);
            ScaleFrame(
                audio,
                frameCount - frame - 1,
                format,
                fadeOut
            );
        }

        return audio;
    }

    private static void ScaleFrame(
        Span<byte> audio,
        int frame,
        PcmAudioFormat format,
        double scale
    )
    {
        var frameOffset = frame * format.BlockAlign;
        for (var channel = 0; channel < format.Channels; channel++)
        {
            var sampleOffset = frameOffset + (channel * sizeof(short));
            var sample = BitConverter.ToInt16(
                audio.Slice(sampleOffset, sizeof(short))
            );
            var scaled = (short)Math.Clamp(
                Math.Round(sample * scale),
                short.MinValue,
                short.MaxValue
            );
            BitConverter.TryWriteBytes(
                audio.Slice(sampleOffset, sizeof(short)),
                scaled
            );
        }
    }

    private static void Encode(
        string outputPath,
        ReadOnlySpan<byte> audio,
        PcmAudioFormat format,
        CancellationToken cancellationToken
    )
    {
        var waveFormat = new WaveFormat(
            format.SampleRate,
            format.BitsPerSample,
            format.Channels
        );
        using var writer = new LameMP3FileWriter(
            outputPath,
            waveFormat,
            BitRate
        );
        const int writeSize = 64 * 1024;
        var offset = 0;
        while (offset < audio.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(writeSize, audio.Length - offset);
            count -= count % format.BlockAlign;
            if (count == 0)
            {
                count = audio.Length - offset;
            }

            writer.Write(audio.Slice(offset, count));
            offset += count;
        }
    }

    private static void WriteTags(
        string outputPath,
        ClipExportMetadata metadata
    )
    {
        using var file = TagLib.File.Create(outputPath);
        file.RemoveTags(TagLib.TagTypes.Id3v1);
        var tag = (TagLib.Id3v2.Tag)file.GetTag(
            TagLib.TagTypes.Id3v2,
            create: true
        );
        tag.Version = 4;
        tag.Title = NullIfWhiteSpace(metadata.Title);
        tag.Performers = string.IsNullOrWhiteSpace(metadata.Artist)
            ? Array.Empty<string>()
            : [metadata.Artist.Trim()];
        tag.Album = NullIfWhiteSpace(metadata.Album);

        if (!metadata.AlbumArt.IsEmpty)
        {
            var picture = new TagLib.Picture(
                new TagLib.ByteVector(metadata.AlbumArt.ToArray())
            )
            {
                Type = TagLib.PictureType.FrontCover,
                Description = "Cover",
                MimeType = DetectImageMimeType(metadata.AlbumArt.Span),
            };
            tag.Pictures = [picture];
        }

        file.Save();
    }

    private static string MoveToAvailableName(
        string temporaryPath,
        string outputFolder,
        string fileStem,
        CancellationToken cancellationToken
    )
    {
        for (var suffix = 1; suffix < int.MaxValue; suffix++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = suffix == 1
                ? $"{fileStem}.mp3"
                : $"{fileStem} ({suffix}).mp3";
            var candidate = Path.Combine(outputFolder, fileName);
            try
            {
                File.Move(temporaryPath, candidate);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
            }
        }

        throw new IOException(
            "No collision-safe export filename was available."
        );
    }

    private static string BuildFileStem(ClipExportMetadata metadata)
    {
        var title = metadata.Title.Trim();
        var artist = metadata.Artist.Trim();
        var rawName = (artist.Length, title.Length) switch
        {
            ( > 0, > 0) => $"{artist} - {title}",
            (_, > 0) => title,
            ( > 0, _) => artist,
            _ => "Untitled clip",
        };

        var builder = new StringBuilder(rawName.Length);
        var previousWasSpace = false;
        foreach (var character in rawName)
        {
            var replacement =
                character < ' '
                || InvalidFileNameCharacters.Contains(character)
                    ? '-'
                    : character;
            if (char.IsWhiteSpace(replacement))
            {
                if (previousWasSpace)
                {
                    continue;
                }

                replacement = ' ';
                previousWasSpace = true;
            }
            else
            {
                previousWasSpace = false;
            }

            builder.Append(replacement);
        }

        var result = builder
            .ToString()
            .Trim()
            .TrimEnd('.', ' ');
        if (result.Length == 0)
        {
            result = "Untitled clip";
        }

        if (result.Length > 120)
        {
            result = result[..120].TrimEnd('.', ' ');
        }

        var firstNamePart = result.Split('.', 2)[0];
        if (ReservedFileNames.Contains(firstNamePart))
        {
            result = $"_{result}";
        }

        return result;
    }

    private static string DetectImageMimeType(
        ReadOnlySpan<byte> bytes
    )
    {
        if (
            bytes.Length >= 8
            && bytes[..8].SequenceEqual(
                new byte[]
                {
                    0x89,
                    0x50,
                    0x4E,
                    0x47,
                    0x0D,
                    0x0A,
                    0x1A,
                    0x0A,
                }
            )
        )
        {
            return "image/png";
        }

        if (
            bytes.Length >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF
        )
        {
            return "image/jpeg";
        }

        if (
            bytes.Length >= 6
            && (bytes[..6].SequenceEqual("GIF87a"u8)
                || bytes[..6].SequenceEqual("GIF89a"u8))
        )
        {
            return "image/gif";
        }

        return "application/octet-stream";
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Preserve the original export result or exception.
        }
    }
}
