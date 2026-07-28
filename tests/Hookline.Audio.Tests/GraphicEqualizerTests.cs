namespace Hookline.Audio.Tests;

public sealed class GraphicEqualizerTests
{
    [Fact]
    public void UsesTheStandardTenIsoCenterFrequencies()
    {
        Assert.Equal(
            [31, 62, 125, 250, 500, 1_000, 2_000, 4_000, 8_000, 16_000],
            GraphicEqualizerBands.CenterFrequencies
        );
        Assert.Equal(-12, GraphicEqualizerBands.MinimumGainDecibels);
        Assert.Equal(12, GraphicEqualizerBands.MaximumGainDecibels);
    }

    [Fact]
    public void RequiredPresetsHaveDistinctDocumentedCurves()
    {
        var expected = new Dictionary<EqualizerPreset, double[]>
        {
            [EqualizerPreset.Flat] =
                [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            [EqualizerPreset.BassBoost] =
                [6, 6, 4, 2, 0, 0, 0, 0, 0, 0],
            [EqualizerPreset.TrebleBoost] =
                [0, 0, 0, 0, 0, 0, 2, 4, 6, 6],
            [EqualizerPreset.Vocal] =
                [-4, -3, -2, 0, 2, 4, 5, 3, 0, -1],
            [EqualizerPreset.Bright] =
                [-1, 0, 0, 0, 1, 2, 3, 4, 5, 4],
            [EqualizerPreset.Mellow] =
                [2, 2, 1, 1, 0, 0, -1, -3, -5, -6],
        };

        Assert.Equal(
            expected.Keys,
            EqualizerPresetCatalog.SelectablePresets
        );
        foreach (var (preset, gains) in expected)
        {
            Assert.Equal(
                gains,
                EqualizerPresetCatalog.GetCurve(preset).Gains
            );
        }

        Assert.Equal(
            expected.Count,
            expected.Keys
                .Select(EqualizerPresetCatalog.GetCurve)
                .Distinct()
                .Count()
        );
    }

    [Fact]
    public void ManualAdjustmentCreatesCustomStateWithoutChangingOtherBands()
    {
        var bass = GraphicEqualizerSelection.FromPreset(
            EqualizerPreset.BassBoost
        );

        var custom = bass.AdjustBand(4, -3);

        Assert.Equal(EqualizerPreset.Custom, custom.Preset);
        Assert.Equal(-3, custom.Curve[4]);
        Assert.Equal(
            bass.Curve.Gains.Take(4),
            custom.Curve.Gains.Take(4)
        );
        Assert.Equal(
            bass.Curve.Gains.Skip(5),
            custom.Curve.Gains.Skip(5)
        );
    }

    [Fact]
    public void SelectingAnotherPresetFullyReplacesACustomCurve()
    {
        var custom = GraphicEqualizerSelection
            .FromPreset(EqualizerPreset.Vocal)
            .AdjustBand(0, 12);

        var mellow = GraphicEqualizerSelection.FromPreset(
            EqualizerPreset.Mellow
        );

        Assert.Equal(EqualizerPreset.Custom, custom.Preset);
        Assert.Equal(EqualizerPreset.Mellow, mellow.Preset);
        Assert.Equal(
            EqualizerPresetCatalog.GetCurve(
                EqualizerPreset.Mellow
            ),
            mellow.Curve
        );
    }

    [Fact]
    public void CurveRejectsWrongBandCountsAndOutOfRangeGains()
    {
        Assert.Throws<ArgumentException>(
            () => new GraphicEqualizerCurve(new double[9])
        );
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new GraphicEqualizerCurve(
                    Enumerable.Repeat(13d, 10)
                )
        );
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                EqualizerPresetCatalog.GetCurve(
                    EqualizerPreset.Custom
                )
        );
    }
}
