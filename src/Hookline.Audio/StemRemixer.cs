using System.Buffers.Binary;

namespace Hookline.Audio;

public static class StemRemixer
{
    public const double MinimumGain = 0;
    public const double MaximumGain = 1.5;

    public static AudioBufferSnapshot Mix(
        SeparatedStemSet stemSet,
        IReadOnlyDictionary<StemKind, double> gains,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(stemSet);
        ArgumentNullException.ThrowIfNull(gains);
        if (stemSet.Stems.Count == 0)
        {
            throw new ArgumentException(
                AudioStrings.InvalidStemOutput,
                nameof(stemSet)
            );
        }

        var source = stemSet.Source;
        var format = source.Format;
        if (format.BitsPerSample != 16)
        {
            throw new NotSupportedException(
                AudioStrings.UnsupportedStemFormat
            );
        }

        foreach (var stem in stemSet.Stems)
        {
            if (
                stem.Snapshot.Format != format
                || stem.Snapshot.Audio.Length
                    != source.Audio.Length
            )
            {
                throw new ArgumentException(
                    AudioStrings.InvalidStemOutput,
                    nameof(stemSet)
                );
            }
        }

        var normalizedGains = stemSet.Stems
            .Select(stem =>
            {
                var gain = gains.TryGetValue(
                    stem.Kind,
                    out var configured
                )
                    ? configured
                    : 1d;
                if (
                    !double.IsFinite(gain)
                    || gain < MinimumGain
                    || gain > MaximumGain
                )
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(gains)
                    );
                }

                return gain;
            })
            .ToArray();
        var output = new byte[
            source.Audio.Length
                - (source.Audio.Length % format.BlockAlign)
        ];
        var sampleCount = output.Length / sizeof(short);
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            if ((sampleIndex & 0x7FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var offset = sampleIndex * sizeof(short);
            double mixed = 0;
            for (
                var stemIndex = 0;
                stemIndex < stemSet.Stems.Count;
                stemIndex++
            )
            {
                var sample =
                    BinaryPrimitives.ReadInt16LittleEndian(
                        stemSet.Stems[stemIndex]
                            .Snapshot.Audio.Span.Slice(
                                offset,
                                sizeof(short)
                            )
                    );
                mixed += sample * normalizedGains[stemIndex];
            }

            BinaryPrimitives.WriteInt16LittleEndian(
                output.AsSpan(offset, sizeof(short)),
                (short)Math.Clamp(
                    Math.Round(mixed),
                    short.MinValue,
                    short.MaxValue
                )
            );
        }

        return source with
        {
            Audio = output,
        };
    }
}
