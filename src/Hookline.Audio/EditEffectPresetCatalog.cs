namespace Hookline.Audio;

public static class EditEffectPresetCatalog
{
    private static readonly EditEffectPresetValues SlowedReverbValues =
        new(0.8, 0.55, 0);
    private static readonly EditEffectPresetValues SpedUpValues =
        new(1.35, 0, 0);
    private static readonly EditEffectPresetValues EightDAudioValues =
        new(1, 0.2, 0.1);

    public static IReadOnlyList<EditEffectPreset> SelectablePresets
    { get; } =
        Array.AsReadOnly(
            new[]
            {
                EditEffectPreset.SlowedReverb,
                EditEffectPreset.SpedUp,
                EditEffectPreset.EightDAudio,
            }
        );

    public static EditEffectPresetValues GetValues(
        EditEffectPreset preset
    ) =>
        preset switch
        {
            EditEffectPreset.SlowedReverb =>
                SlowedReverbValues,
            EditEffectPreset.SpedUp => SpedUpValues,
            EditEffectPreset.EightDAudio => EightDAudioValues,
            _ => throw new ArgumentOutOfRangeException(
                nameof(preset),
                AudioStrings.InvalidEditEffectPreset
            ),
        };
}

public sealed record EditEffectPresetValues(
    double SpeedMultiplier,
    double ReverbWetMix,
    double RotationRateHertz
);
