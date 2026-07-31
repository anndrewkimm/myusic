namespace Hookline.Audio;

public sealed record UrlVideoMetadata
{
    public required Uri SourceUrl { get; init; }

    public required string VideoId { get; init; }

    public required string Title { get; init; }

    public required string Channel { get; init; }

    public required TimeSpan Duration { get; init; }

    public required string AudioFileExtension { get; init; }

    public ReadOnlyMemory<byte> Thumbnail { get; init; }
}
