namespace Hookline.App.Tests;

public sealed class StemBandPositionMapperTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(25, 15)]
    [InlineData(100, 60)]
    [InlineData(150, 90)]
    public void GainMapsMonotonicallyAcrossTheWholeTrack(
        double gainPercent,
        double expectedPosition
    )
    {
        var position = StemBandPositionMapper.GainToPosition(
            gainPercent,
            trackLength: 90
        );

        Assert.Equal(expectedPosition, position, precision: 10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(49)]
    [InlineData(100)]
    [InlineData(149)]
    [InlineData(150)]
    public void MappingRoundTripsEveryStemValueExactly(
        double gainPercent
    )
    {
        const double trackLength = 137;

        var position = StemBandPositionMapper.GainToPosition(
            gainPercent,
            trackLength
        );
        var roundTripped =
            StemBandPositionMapper.PositionToGain(
                position,
                trackLength
            );

        Assert.Equal(gainPercent, roundTripped, precision: 10);
    }

    [Fact]
    public void PointerPositionIsClampedToStemGainRange()
    {
        Assert.Equal(
            StemBandPositionMapper.MinimumGainPercent,
            StemBandPositionMapper.PositionToGain(-10, 100)
        );
        Assert.Equal(
            StemBandPositionMapper.MaximumGainPercent,
            StemBandPositionMapper.PositionToGain(125, 100)
        );
    }
}
