namespace Hookline.Audio;

public sealed record GraphicEqualizerSelection
{
    private GraphicEqualizerSelection(
        EqualizerPreset preset,
        GraphicEqualizerCurve curve
    )
    {
        Preset = preset;
        Curve = curve;
    }

    public static GraphicEqualizerSelection Flat { get; } =
        FromPreset(EqualizerPreset.Flat);

    public EqualizerPreset Preset { get; }

    public GraphicEqualizerCurve Curve { get; }

    public static GraphicEqualizerSelection FromPreset(
        EqualizerPreset preset
    ) =>
        new(preset, EqualizerPresetCatalog.GetCurve(preset));

    public GraphicEqualizerSelection AdjustBand(
        int bandIndex,
        double gainDecibels
    )
    {
        var curve = Curve.WithGain(bandIndex, gainDecibels);
        return curve.Equals(Curve)
            ? this
            : new GraphicEqualizerSelection(
                EqualizerPreset.Custom,
                curve
            );
    }
}
