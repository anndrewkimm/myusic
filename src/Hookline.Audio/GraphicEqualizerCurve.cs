using System.Collections.ObjectModel;

namespace Hookline.Audio;

public sealed class GraphicEqualizerCurve
    : IEquatable<GraphicEqualizerCurve>
{
    private readonly double[] _gains;
    private readonly ReadOnlyCollection<double> _readOnlyGains;

    public GraphicEqualizerCurve(IEnumerable<double> gains)
    {
        ArgumentNullException.ThrowIfNull(gains);
        _gains = gains.ToArray();
        if (
            _gains.Length
            != GraphicEqualizerBands.CenterFrequencies.Count
        )
        {
            throw new ArgumentException(
                AudioStrings.InvalidEqualizerBandCount,
                nameof(gains)
            );
        }

        if (
            _gains.Any(
                gain =>
                    !double.IsFinite(gain)
                    || gain
                        < GraphicEqualizerBands
                            .MinimumGainDecibels
                    || gain
                        > GraphicEqualizerBands
                            .MaximumGainDecibels
            )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(gains),
                AudioStrings.InvalidEqualizerGain
            );
        }

        _readOnlyGains = Array.AsReadOnly(_gains);
    }

    public static GraphicEqualizerCurve Flat { get; } =
        new(new double[GraphicEqualizerBands.CenterFrequencies.Count]);

    public IReadOnlyList<double> Gains => _readOnlyGains;

    public double this[int bandIndex] => _gains[bandIndex];

    public bool IsFlat => _gains.All(gain => gain == 0);

    public GraphicEqualizerCurve WithGain(
        int bandIndex,
        double gainDecibels
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bandIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            bandIndex,
            _gains.Length
        );
        var gains = _gains.ToArray();
        gains[bandIndex] = gainDecibels;
        return new GraphicEqualizerCurve(gains);
    }

    public bool Equals(GraphicEqualizerCurve? other) =>
        other is not null
        && _gains.SequenceEqual(other._gains);

    public override bool Equals(object? obj) =>
        obj is GraphicEqualizerCurve other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var gain in _gains)
        {
            hash.Add(gain);
        }

        return hash.ToHashCode();
    }
}
