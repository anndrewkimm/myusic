namespace Hookline.Audio;

public sealed record ClipExportResult
{
    public required string OutputPath { get; init; }

    public required TimeSpan Duration { get; init; }
}
