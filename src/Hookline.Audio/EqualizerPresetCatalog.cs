namespace Hookline.Audio;

public static class EqualizerPresetCatalog
{
    private static readonly GraphicEqualizerCurve BassBoostCurve =
        new([6, 6, 4, 2, 0, 0, 0, 0, 0, 0]);
    private static readonly GraphicEqualizerCurve TrebleBoostCurve =
        new([0, 0, 0, 0, 0, 0, 2, 4, 6, 6]);
    private static readonly GraphicEqualizerCurve VocalCurve =
        new([-4, -3, -2, 0, 2, 4, 5, 3, 0, -1]);
    private static readonly GraphicEqualizerCurve BrightCurve =
        new([-1, 0, 0, 0, 1, 2, 3, 4, 5, 4]);
    private static readonly GraphicEqualizerCurve MellowCurve =
        new([2, 2, 1, 1, 0, 0, -1, -3, -5, -6]);

    public static IReadOnlyList<EqualizerPreset> SelectablePresets
    { get; } =
        Array.AsReadOnly(
            new[]
            {
                EqualizerPreset.Flat,
                EqualizerPreset.BassBoost,
                EqualizerPreset.TrebleBoost,
                EqualizerPreset.Vocal,
                EqualizerPreset.Bright,
                EqualizerPreset.Mellow,
            }
        );

    public static GraphicEqualizerCurve GetCurve(
        EqualizerPreset preset
    ) =>
        preset switch
        {
            EqualizerPreset.Flat => GraphicEqualizerCurve.Flat,
            EqualizerPreset.BassBoost => BassBoostCurve,
            EqualizerPreset.TrebleBoost => TrebleBoostCurve,
            EqualizerPreset.Vocal => VocalCurve,
            EqualizerPreset.Bright => BrightCurve,
            EqualizerPreset.Mellow => MellowCurve,
            _ => throw new ArgumentOutOfRangeException(
                nameof(preset),
                AudioStrings.InvalidEqualizerPreset
            ),
        };
}
