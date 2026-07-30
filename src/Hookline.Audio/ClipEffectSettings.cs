namespace Hookline.Audio;

public sealed record ClipEffectSettings
{
    public const double MinimumSpeedMultiplier = 0.5;
    public const double MaximumSpeedMultiplier = 2;
    public const double MinimumReverbWetMix = 0;
    public const double MaximumReverbWetMix = 1;
    public const double MinimumRotationRateHertz = 0.05;
    public const double MaximumRotationRateHertz = 0.25;
    public const int MinimumLoopCount = 1;
    public const int MaximumLoopCount = 64;

    public static readonly TimeSpan MaximumExpandedDuration =
        TimeSpan.FromMinutes(5);

    public double SpeedMultiplier { get; init; } = 1;

    public GraphicEqualizerCurve EqualizerCurve { get; init; } =
        GraphicEqualizerCurve.Flat;

    public double ReverbWetMix { get; init; }

    public int LoopCount { get; init; } = MinimumLoopCount;

    public double RotationRateHertz { get; init; }

    public bool IsNeutral =>
        SpeedMultiplier == 1
        && EqualizerCurve is { IsFlat: true }
        && ReverbWetMix == MinimumReverbWetMix
        && LoopCount == MinimumLoopCount
        && RotationRateHertz == 0;

    internal static void ValidateSpeed(double speedMultiplier)
    {
        if (
            !double.IsFinite(speedMultiplier)
            || speedMultiplier < MinimumSpeedMultiplier
            || speedMultiplier > MaximumSpeedMultiplier
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(speedMultiplier),
                AudioStrings.InvalidEffectSpeed
            );
        }
    }

    internal static void ValidateReverb(double reverbWetMix)
    {
        if (
            !double.IsFinite(reverbWetMix)
            || reverbWetMix < MinimumReverbWetMix
            || reverbWetMix > MaximumReverbWetMix
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(reverbWetMix),
                AudioStrings.InvalidReverbAmount
            );
        }
    }

    internal static void ValidateRotation(double rotationRateHertz)
    {
        if (
            !double.IsFinite(rotationRateHertz)
            || rotationRateHertz < 0
            || (
                rotationRateHertz > 0
                && rotationRateHertz
                    < MinimumRotationRateHertz
            )
            || rotationRateHertz > MaximumRotationRateHertz
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(rotationRateHertz),
                AudioStrings.InvalidRotationRate
            );
        }
    }
}
