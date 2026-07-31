namespace Hookline.Audio;

public sealed record ClipSegmentRenderSettings
{
    public required TimeSpan Start { get; init; }

    public required TimeSpan End { get; init; }

    public required ClipEffectSettings Effects { get; init; }

    public IReadOnlyDictionary<StemKind, double> StemGains { get; init; } =
        new Dictionary<StemKind, double>();
}
