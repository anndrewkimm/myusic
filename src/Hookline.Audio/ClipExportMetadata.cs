namespace Hookline.Audio;

public sealed record ClipExportMetadata
{
    public required string Title { get; init; }

    public required string Artist { get; init; }

    public required string Album { get; init; }

    public ReadOnlyMemory<byte> AlbumArt { get; init; }
}
