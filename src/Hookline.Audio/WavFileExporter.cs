using System.Buffers.Binary;

namespace Hookline.Audio;

public static class WavFileExporter
{
    private const int HeaderSize = 44;

    public static async Task WriteAsync(
        string outputPath,
        AudioBufferSnapshot snapshot,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(snapshot);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var header = CreateHeader(
            snapshot.Format,
            snapshot.Audio.Length
        );
        await using var output = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            useAsync: true
        );
        await output
            .WriteAsync(header, cancellationToken)
            .ConfigureAwait(false);
        await output
            .WriteAsync(snapshot.Audio, cancellationToken)
            .ConfigureAwait(false);
        await output.FlushAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static byte[] CreateHeader(
        PcmAudioFormat format,
        int audioLength
    )
    {
        var header = new byte[HeaderSize];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(4),
            checked((uint)(audioLength + HeaderSize - 8))
        );
        "WAVE"u8.CopyTo(header.AsSpan(8));
        "fmt "u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(16),
            16
        );
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(20),
            1
        );
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(22),
            checked((ushort)format.Channels)
        );
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(24),
            checked((uint)format.SampleRate)
        );
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(28),
            checked((uint)format.AverageBytesPerSecond)
        );
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(32),
            checked((ushort)format.BlockAlign)
        );
        BinaryPrimitives.WriteUInt16LittleEndian(
            header.AsSpan(34),
            checked((ushort)format.BitsPerSample)
        );
        "data"u8.CopyTo(header.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(40),
            checked((uint)audioLength)
        );
        return header;
    }
}
