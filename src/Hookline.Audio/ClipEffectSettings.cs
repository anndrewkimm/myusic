namespace Hookline.Audio;

public sealed record ClipEffectSettings
{
    public const double MinimumSpeedMultiplier = 0.5;
    public const double MaximumSpeedMultiplier = 2;
    public const int MinimumLoopCount = 1;
    public const int MaximumLoopCount = 64;

    public static readonly TimeSpan MaximumExpandedDuration =
        TimeSpan.FromMinutes(5);

    public double SpeedMultiplier { get; init; } = 1;

    public GraphicEqualizerCurve EqualizerCurve { get; init; } =
        GraphicEqualizerCurve.Flat;

    public int LoopCount { get; init; } = MinimumLoopCount;

    public bool IsNeutral =>
        SpeedMultiplier == 1
        && EqualizerCurve is { IsFlat: true }
        && LoopCount == MinimumLoopCount;
}
