namespace Hookline.Audio;

public sealed record ImportedAudioFile
{
    public required string SourcePath { get; init; }

    public required AudioBufferSnapshot Snapshot { get; init; }

    public required ClipExportMetadata Metadata { get; init; }
}
