namespace Hookline.App;

internal static class PreviewResumePositionMapper
{
    public static TimeSpan Map(
        TimeSpan currentPosition,
        TimeSpan previousDuration,
        TimeSpan newDuration
    )
    {
        if (
            currentPosition <= TimeSpan.Zero
            || previousDuration <= TimeSpan.Zero
            || newDuration <= TimeSpan.Zero
        )
        {
            return TimeSpan.Zero;
        }

        var clampedPosition = currentPosition < previousDuration
            ? currentPosition
            : previousDuration;
        var positionFraction =
            (decimal)clampedPosition.Ticks
            / previousDuration.Ticks;
        var mappedTicks = decimal.ToInt64(
            decimal.Round(
                positionFraction * newDuration.Ticks,
                MidpointRounding.AwayFromZero
            )
        );
        return TimeSpan.FromTicks(mappedTicks);
    }
}
