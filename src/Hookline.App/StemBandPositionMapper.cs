using Hookline.Audio;

namespace Hookline.App;

internal static class StemBandPositionMapper
{
    public const double MinimumGainPercent =
        StemRemixer.MinimumGain * 100;
    public const double MaximumGainPercent =
        StemRemixer.MaximumGain * 100;

    public static double GainToPosition(
        double gainPercent,
        double trackLength
    )
    {
        if (!double.IsFinite(trackLength) || trackLength <= 0)
        {
            return 0;
        }

        var normalizedGain = NormalizeGain(gainPercent);
        return trackLength
            * (
                (normalizedGain - MinimumGainPercent)
                / (MaximumGainPercent - MinimumGainPercent)
            );
    }

    public static double PositionToGain(
        double position,
        double trackLength
    )
    {
        if (!double.IsFinite(trackLength) || trackLength <= 0)
        {
            return MinimumGainPercent;
        }

        var normalizedPosition = double.IsFinite(position)
            ? Math.Clamp(position, 0, trackLength)
            : 0;
        return MinimumGainPercent
            + (
                (normalizedPosition / trackLength)
                * (MaximumGainPercent - MinimumGainPercent)
            );
    }

    private static double NormalizeGain(double gainPercent) =>
        double.IsFinite(gainPercent)
            ? Math.Clamp(
                gainPercent,
                MinimumGainPercent,
                MaximumGainPercent
            )
            : MinimumGainPercent;
}
