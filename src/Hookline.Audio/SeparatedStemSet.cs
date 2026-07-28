namespace Hookline.Audio;

public sealed record SeparatedStem
{
    public required StemKind Kind { get; init; }

    public required AudioBufferSnapshot Snapshot { get; init; }
}

public sealed record SeparatedStemSet
{
    public required StemSeparationMode Mode { get; init; }

    public required AudioBufferSnapshot Source { get; init; }

    public required IReadOnlyList<SeparatedStem> Stems { get; init; }
}
