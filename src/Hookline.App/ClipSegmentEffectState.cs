using Hookline.Audio;

namespace Hookline.App;

internal sealed class ClipSegmentEffectState
{
    private readonly Dictionary<StemKind, double> _stemVolumePercents =
        new();

    public EditEffectSelection EditEffectSelection { get; set; } =
        EditEffectSelection.None;

    public GraphicEqualizerSelection EqualizerSelection { get; set; } =
        GraphicEqualizerSelection.Flat;

    public int LoopCount { get; set; } =
        ClipEffectSettings.MinimumLoopCount;

    public ClipSegmentEffectState Clone()
    {
        var clone = new ClipSegmentEffectState
        {
            EditEffectSelection = EditEffectSelection,
            EqualizerSelection = EqualizerSelection,
            LoopCount = LoopCount,
        };
        foreach (var pair in _stemVolumePercents)
        {
            clone._stemVolumePercents[pair.Key] = pair.Value;
        }

        return clone;
    }

    public double GetStemVolumePercent(StemKind kind) =>
        _stemVolumePercents.TryGetValue(kind, out var value)
            ? value
            : 100d;

    public void SetStemVolumePercent(StemKind kind, double value) =>
        _stemVolumePercents[kind] = value;

    public void ClearStemVolumes() => _stemVolumePercents.Clear();

    public ClipSegmentRenderSettings CreateRenderSettings(
        TimeSpan start,
        TimeSpan end
    ) =>
        new()
        {
            Start = start,
            End = end,
            Effects = new ClipEffectSettings
            {
                SpeedMultiplier = EditEffectSelection.SpeedMultiplier,
                EqualizerCurve = EqualizerSelection.Curve,
                ReverbWetMix = EditEffectSelection.ReverbWetMix,
                LoopCount = LoopCount,
                RotationRateHertz =
                    EditEffectSelection.RotationRateHertz,
            },
            StemGains = _stemVolumePercents.ToDictionary(
                pair => pair.Key,
                pair => pair.Value / 100d
            ),
        };
}
