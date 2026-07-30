namespace Hookline.Audio.Tests;

public sealed class EditEffectPresetTests
{
    [Fact]
    public void RequiredPresetsHaveDistinctDocumentedValues()
    {
        var expected =
            new Dictionary<
                EditEffectPreset,
                EditEffectPresetValues
            >
            {
                [EditEffectPreset.SlowedReverb] =
                    new(0.8, 0.55, 0),
                [EditEffectPreset.SpedUp] =
                    new(1.35, 0, 0),
                [EditEffectPreset.EightDAudio] =
                    new(1, 0.2, 0.1),
            };

        Assert.Equal(
            expected.Keys,
            EditEffectPresetCatalog.SelectablePresets
        );
        foreach (var (preset, values) in expected)
        {
            Assert.Equal(
                values,
                EditEffectPresetCatalog.GetValues(preset)
            );
        }

        Assert.Equal(
            expected.Count,
            expected.Values.Distinct().Count()
        );
    }

    [Fact]
    public void ManualAdjustmentOfAnyPresetControlCreatesCustomState()
    {
        var slowed = EditEffectSelection.FromPreset(
            EditEffectPreset.SlowedReverb
        );

        Assert.Equal(
            EditEffectPreset.Custom,
            slowed.AdjustSpeed(0.85).Preset
        );
        Assert.Equal(
            EditEffectPreset.Custom,
            slowed.AdjustReverb(0.5).Preset
        );
        Assert.Equal(
            EditEffectPreset.Custom,
            slowed.AdjustRotation(0.1).Preset
        );
    }

    [Fact]
    public void SelectingAnotherPresetFullyReplacesCustomValues()
    {
        var custom = EditEffectSelection
            .FromPreset(EditEffectPreset.EightDAudio)
            .AdjustSpeed(1.2)
            .AdjustReverb(0.75)
            .AdjustRotation(0.2);

        var spedUp = EditEffectSelection.FromPreset(
            EditEffectPreset.SpedUp
        );

        Assert.Equal(EditEffectPreset.Custom, custom.Preset);
        Assert.Equal(EditEffectPreset.SpedUp, spedUp.Preset);
        Assert.Equal(1.35, spedUp.SpeedMultiplier);
        Assert.Equal(0, spedUp.ReverbWetMix);
        Assert.Equal(0, spedUp.RotationRateHertz);
    }

    [Fact]
    public void NonSelectableStatesAreRejectedAsPresets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                EditEffectSelection.FromPreset(
                    EditEffectPreset.None
                )
        );
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                EditEffectSelection.FromPreset(
                    EditEffectPreset.Custom
                )
        );
    }
}
