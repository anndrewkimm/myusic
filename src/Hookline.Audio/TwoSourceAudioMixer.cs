using System.Buffers.Binary;

namespace Hookline.Audio;

public static class TwoSourceAudioMixer
{
    public const long MixedTrackInstanceId = long.MinValue;

    public static AudioBufferSnapshot Mix(
        AudioBufferSnapshot first,
        double firstGain,
        AudioBufferSnapshot second,
        double secondGain,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ValidateGain(firstGain, nameof(firstGain));
        ValidateGain(secondGain, nameof(secondGain));

        if (first.Audio.IsEmpty || second.Audio.IsEmpty)
        {
            throw new ArgumentException(AudioStrings.MixSourceEmpty);
        }

        if (first.Format != second.Format)
        {
            throw new ArgumentException(
                AudioStrings.MixFormatMismatch
            );
        }

        var format = first.Format;
        if (format.BitsPerSample != 16)
        {
            throw new NotSupportedException(
                AudioStrings.UnsupportedEffectsFormat
            );
        }

        var firstFrameCount =
            first.Audio.Length / format.BlockAlign;
        var secondFrameCount =
            second.Audio.Length / format.BlockAlign;
        var outputFrameCount = Math.Max(
            firstFrameCount,
            secondFrameCount
        );
        var maximumFrameCount = (int)Math.Floor(
            ClipEffectSettings.MaximumExpandedDuration.TotalSeconds
                * format.SampleRate
        );
        if (outputFrameCount > maximumFrameCount)
        {
            throw new InvalidOperationException(
                AudioStrings.MixedOutputTooLong
            );
        }

        cancellationToken.ThrowIfCancellationRequested();
        var firstAudio = LoopToLength(
            first.Audio.Span,
            format,
            outputFrameCount,
            cancellationToken
        );
        var secondAudio = LoopToLength(
            second.Audio.Span,
            format,
            outputFrameCount,
            cancellationToken
        );
        var output = new byte[
            checked(outputFrameCount * format.BlockAlign)
        ];
        var sampleCount = output.Length / sizeof(short);
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            if ((sampleIndex & 0x7FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var offset = sampleIndex * sizeof(short);
            var firstSample =
                BinaryPrimitives.ReadInt16LittleEndian(
                    firstAudio.Slice(offset, sizeof(short))
                );
            var secondSample =
                BinaryPrimitives.ReadInt16LittleEndian(
                    secondAudio.Slice(offset, sizeof(short))
                );
            var mixed = (firstSample * firstGain)
                + (secondSample * secondGain);
            BinaryPrimitives.WriteInt16LittleEndian(
                output.AsSpan(offset, sizeof(short)),
                (short)Math.Clamp(
                    Math.Round(mixed),
                    short.MinValue,
                    short.MaxValue
                )
            );
        }

        var duration = format.GetDuration(output.Length);
        return new AudioBufferSnapshot
        {
            TrackInstanceId = MixedTrackInstanceId,
            Format = format,
            Audio = output,
            RequestedStart = TimeSpan.Zero,
            RequestedEnd = duration,
            AvailableStart = TimeSpan.Zero,
            AvailableEnd = duration,
            IncludedRanges =
            [
                new AudioTimeRange(TimeSpan.Zero, duration),
            ],
        };
    }

    private static ReadOnlySpan<byte> LoopToLength(
        ReadOnlySpan<byte> source,
        PcmAudioFormat format,
        int outputFrameCount,
        CancellationToken cancellationToken
    )
    {
        var sourceFrameCount = source.Length / format.BlockAlign;
        if (sourceFrameCount == outputFrameCount)
        {
            return source[..(outputFrameCount * format.BlockAlign)];
        }

        var crossfadeFrameCount =
            ClipEffectsProcessor.GetLoopCrossfadeFrameCount(
                sourceFrameCount,
                format.SampleRate
            );
        var framesPerAdditionalLoop =
            sourceFrameCount - crossfadeFrameCount;
        var additionalFrames = outputFrameCount - sourceFrameCount;
        var additionalLoops = checked(
            (additionalFrames + framesPerAdditionalLoop - 1)
                / framesPerAdditionalLoop
        );
        return ClipEffectsProcessor.AssembleLoops(
            source,
            format,
            additionalLoops + 1,
            outputFrameCount,
            crossfadeFrameCount,
            cancellationToken
        );
    }

    private static void ValidateGain(double gain, string parameterName)
    {
        if (
            !double.IsFinite(gain)
            || gain < StemRemixer.MinimumGain
            || gain > StemRemixer.MaximumGain
        )
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
