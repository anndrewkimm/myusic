namespace Hookline.Audio;

public sealed record StemModelDescriptor
{
    public required StemSeparationMode Mode { get; init; }

    public required string DisplayName { get; init; }

    public required string FileName { get; init; }

    public required Uri DownloadUri { get; init; }

    public required string DisplayDownloadSize { get; init; }

    public required string Sha256 { get; init; }

    public required IReadOnlyList<StemKind> Stems { get; init; }
}
