namespace Hookline.App.Catalog;

public sealed record ClipCatalogEntry
{
    public required Guid Id { get; init; }

    public required string DisplayTitle { get; init; }

    public required string SourceTitle { get; init; }

    public required string SourceArtist { get; init; }

    public required string SourceAlbum { get; init; }

    public required DateTimeOffset ExportedAt { get; init; }

    public required string FilePath { get; init; }

    public required TimeSpan TrimStart { get; init; }

    public required TimeSpan TrimEnd { get; init; }

    public required TimeSpan Duration { get; init; }

    public required long TrackInstanceId { get; init; }

    public byte[] AlbumArt { get; init; } = [];

    public bool IsMissing { get; init; }
}
