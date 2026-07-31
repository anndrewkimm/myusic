namespace Hookline.App.Tests;

public sealed class PreviewResumePositionMapperTests
{
    [Theory]
    [InlineData(250, 1000, 500, 125)]
    [InlineData(250, 500, 1000, 500)]
    [InlineData(1200, 1000, 500, 500)]
    [InlineData(-100, 1000, 500, 0)]
    [InlineData(250, 0, 500, 0)]
    [InlineData(250, 1000, 0, 0)]
    public void MapsPositionByTheFractionOfTheProcessedBuffer(
        int currentPositionMilliseconds,
        int previousDurationMilliseconds,
        int newDurationMilliseconds,
        int expectedPositionMilliseconds
    )
    {
        var result = PreviewResumePositionMapper.Map(
            TimeSpan.FromMilliseconds(
                currentPositionMilliseconds
            ),
            TimeSpan.FromMilliseconds(
                previousDurationMilliseconds
            ),
            TimeSpan.FromMilliseconds(newDurationMilliseconds)
        );

        Assert.Equal(
            TimeSpan.FromMilliseconds(expectedPositionMilliseconds),
            result
        );
    }
}
