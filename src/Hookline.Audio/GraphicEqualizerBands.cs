namespace Hookline.Audio;

public static class GraphicEqualizerBands
{
    public const double MinimumGainDecibels = -12;
    public const double MaximumGainDecibels = 12;

    public static IReadOnlyList<int> CenterFrequencies { get; } =
        Array.AsReadOnly(
            new[]
            {
                31,
                62,
                125,
                250,
                500,
                1_000,
                2_000,
                4_000,
                8_000,
                16_000,
            }
        );
}
