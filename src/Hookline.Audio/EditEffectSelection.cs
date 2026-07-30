namespace Hookline.Audio;

public sealed record EditEffectSelection
{
    private EditEffectSelection(
        EditEffectPreset preset,
        double speedMultiplier,
        double reverbWetMix,
        double rotationRateHertz
    )
    {
        Preset = preset;
        SpeedMultiplier = speedMultiplier;
        ReverbWetMix = reverbWetMix;
        RotationRateHertz = rotationRateHertz;
    }

    public static EditEffectSelection None { get; } =
        new(EditEffectPreset.None, 1, 0, 0);

    public EditEffectPreset Preset { get; }

    public double SpeedMultiplier { get; }

    public double ReverbWetMix { get; }

    public double RotationRateHertz { get; }

    public static EditEffectSelection FromPreset(
        EditEffectPreset preset
    )
    {
        var values = EditEffectPresetCatalog.GetValues(preset);
        return new EditEffectSelection(
            preset,
            values.SpeedMultiplier,
            values.ReverbWetMix,
            values.RotationRateHertz
        );
    }

    public EditEffectSelection AdjustSpeed(double speedMultiplier) =>
        speedMultiplier == SpeedMultiplier
            ? this
            : CreateCustom(
                speedMultiplier,
                ReverbWetMix,
                RotationRateHertz
            );

    public EditEffectSelection AdjustReverb(double reverbWetMix) =>
        reverbWetMix == ReverbWetMix
            ? this
            : CreateCustom(
                SpeedMultiplier,
                reverbWetMix,
                RotationRateHertz
            );

    public EditEffectSelection AdjustRotation(
        double rotationRateHertz
    ) =>
        rotationRateHertz == RotationRateHertz
            ? this
            : CreateCustom(
                SpeedMultiplier,
                ReverbWetMix,
                rotationRateHertz
            );

    private static EditEffectSelection CreateCustom(
        double speedMultiplier,
        double reverbWetMix,
        double rotationRateHertz
    )
    {
        ClipEffectSettings.ValidateSpeed(speedMultiplier);
        ClipEffectSettings.ValidateReverb(reverbWetMix);
        ClipEffectSettings.ValidateRotation(rotationRateHertz);
        return new EditEffectSelection(
            EditEffectPreset.Custom,
            speedMultiplier,
            reverbWetMix,
            rotationRateHertz
        );
    }
}
