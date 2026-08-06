using System.Buffers.Binary;

namespace Hookline.Audio;

public static class TwoSourceAudioMixer
{
    public const long MixedTrackInstanceId = long.MinValue;

    public static readonly TimeSpan SequentialCrossfadeDuration =
        TimeSpan.FromSeconds(1.5);

    private const double PeakSafetyCeilingDecibels = -1d;

    private static readonly double PeakSafetyCeilingSample =
        short.MaxValue
        * Math.Pow(10d, PeakSafetyCeilingDecibels / 20d);

    public static AudioBufferSnapshot Mix(
        AudioBufferSnapshot first,
        double firstGain,
        AudioBufferSnapshot second,
        double secondGain,
        CancellationToken cancellationToken = default
    )
    {
        var format = ValidateSources(
            first,
            firstGain,
            second,
            secondGain
        );
        var firstFrameCount =
            first.Audio.Length / format.BlockAlign;
        var secondFrameCount =
            second.Audio.Length / format.BlockAlign;
        var outputFrameCount = ValidateOutputFrameCount(
            Math.Max(firstFrameCount, secondFrameCount),
            format
        );

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
        var sampleCount = checked(
            outputFrameCount * format.Channels
        );
        var peakScale = GetOverlayPeakScale(
            firstAudio,
            firstGain,
            secondAudio,
            secondGain,
            sampleCount,
            cancellationToken
        );
        var output = new byte[
            checked(outputFrameCount * format.BlockAlign)
        ];
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            ObserveCancellation(sampleIndex, cancellationToken);
            var offset = sampleIndex * sizeof(short);
            var mixed =
                (ReadSample(firstAudio, offset) * firstGain)
                + (ReadSample(secondAudio, offset) * secondGain);
            WriteSample(output, offset, mixed * peakScale);
        }

        return CreateSnapshot(output, format);
    }

    public static AudioBufferSnapshot ArrangeSequentially(
        AudioBufferSnapshot first,
        double firstGain,
        AudioBufferSnapshot second,
        double secondGain,
        CancellationToken cancellationToken = default
    )
    {
        var format = ValidateSources(
            first,
            firstGain,
            second,
            secondGain
        );
        var firstFrameCount =
            first.Audio.Length / format.BlockAlign;
        var secondFrameCount =
            second.Audio.Length / format.BlockAlign;
        var crossfadeFrameCount =
            format.GetAlignedByteCount(
                SequentialCrossfadeDuration
            ) / format.BlockAlign;
        if (
            firstFrameCount < crossfadeFrameCount
            || secondFrameCount < crossfadeFrameCount
        )
        {
            throw new InvalidOperationException(
                AudioStrings.SequentialMixSourceTooShort
            );
        }

        var outputFrameCount = ValidateOutputFrameCount(
            (long)firstFrameCount
                + secondFrameCount
                - crossfadeFrameCount,
            format
        );
        cancellationToken.ThrowIfCancellationRequested();
        var peakScale = GetSequentialPeakScale(
            first.Audio.Span,
            firstGain,
            firstFrameCount,
            second.Audio.Span,
            secondGain,
            secondFrameCount,
            outputFrameCount,
            crossfadeFrameCount,
            format.Channels,
            cancellationToken
        );
        var output = new byte[
            checked(outputFrameCount * format.BlockAlign)
        ];
        for (var frameIndex = 0; frameIndex < outputFrameCount; frameIndex++)
        {
            ObserveCancellation(frameIndex, cancellationToken);
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var sampleIndex = (frameIndex * format.Channels) + channel;
                var mixed = GetSequentialSample(
                    first.Audio.Span,
                    firstGain,
                    firstFrameCount,
                    second.Audio.Span,
                    secondGain,
                    frameIndex,
                    crossfadeFrameCount,
                    format.Channels,
                    channel
                );
                WriteSample(
                    output,
                    sampleIndex * sizeof(short),
                    mixed * peakScale
                );
            }
        }

        return CreateSnapshot(output, format);
    }

    private static PcmAudioFormat ValidateSources(
        AudioBufferSnapshot first,
        double firstGain,
        AudioBufferSnapshot second,
        double secondGain
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
            throw new ArgumentException(AudioStrings.MixFormatMismatch);
        }

        if (first.Format.BitsPerSample != 16)
        {
            throw new NotSupportedException(
                AudioStrings.UnsupportedEffectsFormat
            );
        }

        return first.Format;
    }

    private static int ValidateOutputFrameCount(
        long outputFrameCount,
        PcmAudioFormat format
    )
    {
        var maximumFrameCount = (long)Math.Floor(
            ClipEffectSettings.MaximumExpandedDuration.TotalSeconds
                * format.SampleRate
        );
        if (outputFrameCount > maximumFrameCount)
        {
            throw new InvalidOperationException(
                AudioStrings.MixedOutputTooLong
            );
        }

        return checked((int)outputFrameCount);
    }

    private static double GetOverlayPeakScale(
        ReadOnlySpan<byte> first,
        double firstGain,
        ReadOnlySpan<byte> second,
        double secondGain,
        int sampleCount,
        CancellationToken cancellationToken
    )
    {
        double minimum = 0;
        double maximum = 0;
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            ObserveCancellation(sampleIndex, cancellationToken);
            var offset = sampleIndex * sizeof(short);
            var mixed =
                (ReadSample(first, offset) * firstGain)
                + (ReadSample(second, offset) * secondGain);
            minimum = Math.Min(minimum, mixed);
            maximum = Math.Max(maximum, mixed);
        }

        return GetPeakSafetyScale(minimum, maximum);
    }

    private static double GetSequentialPeakScale(
        ReadOnlySpan<byte> first,
        double firstGain,
        int firstFrameCount,
        ReadOnlySpan<byte> second,
        double secondGain,
        int secondFrameCount,
        int outputFrameCount,
        int crossfadeFrameCount,
        int channels,
        CancellationToken cancellationToken
    )
    {
        double minimum = 0;
        double maximum = 0;
        for (var frameIndex = 0; frameIndex < outputFrameCount; frameIndex++)
        {
            ObserveCancellation(frameIndex, cancellationToken);
            for (var channel = 0; channel < channels; channel++)
            {
                var mixed = GetSequentialSample(
                    first,
                    firstGain,
                    firstFrameCount,
                    second,
                    secondGain,
                    frameIndex,
                    crossfadeFrameCount,
                    channels,
                    channel
                );
                minimum = Math.Min(minimum, mixed);
                maximum = Math.Max(maximum, mixed);
            }
        }

        return GetPeakSafetyScale(minimum, maximum);
    }

    private static double GetSequentialSample(
        ReadOnlySpan<byte> first,
        double firstGain,
        int firstFrameCount,
        ReadOnlySpan<byte> second,
        double secondGain,
        int outputFrameIndex,
        int crossfadeFrameCount,
        int channels,
        int channel
    )
    {
        var crossfadeStartFrame =
            firstFrameCount - crossfadeFrameCount;
        if (outputFrameIndex < crossfadeStartFrame)
        {
            return ReadFrameSample(
                first,
                outputFrameIndex,
                channels,
                channel
            ) * firstGain;
        }

        if (outputFrameIndex < firstFrameCount)
        {
            var crossfadeFrame =
                outputFrameIndex - crossfadeStartFrame;
            var progress = crossfadeFrameCount <= 1
                ? 1d
                : crossfadeFrame
                    / (double)(crossfadeFrameCount - 1);
            var angle = progress * Math.PI / 2d;
            var firstSample = ReadFrameSample(
                first,
                outputFrameIndex,
                channels,
                channel
            );
            var secondSample = ReadFrameSample(
                second,
                crossfadeFrame,
                channels,
                channel
            );
            return (firstSample * firstGain * Math.Cos(angle))
                + (secondSample * secondGain * Math.Sin(angle));
        }

        var secondFrame = outputFrameIndex - crossfadeStartFrame;
        return ReadFrameSample(
            second,
            secondFrame,
            channels,
            channel
        ) * secondGain;
    }

    private static double GetPeakSafetyScale(
        double minimum,
        double maximum
    )
    {
        if (minimum >= short.MinValue && maximum <= short.MaxValue)
        {
            return 1d;
        }

        var peak = Math.Max(Math.Abs(minimum), Math.Abs(maximum));
        return PeakSafetyCeilingSample / peak;
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

    private static AudioBufferSnapshot CreateSnapshot(
        byte[] output,
        PcmAudioFormat format
    )
    {
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

    private static short ReadFrameSample(
        ReadOnlySpan<byte> audio,
        int frameIndex,
        int channels,
        int channel
    ) =>
        ReadSample(
            audio,
            ((frameIndex * channels) + channel) * sizeof(short)
        );

    private static short ReadSample(
        ReadOnlySpan<byte> audio,
        int offset
    ) =>
        BinaryPrimitives.ReadInt16LittleEndian(
            audio.Slice(offset, sizeof(short))
        );

    private static void WriteSample(
        Span<byte> output,
        int offset,
        double sample
    ) =>
        BinaryPrimitives.WriteInt16LittleEndian(
            output.Slice(offset, sizeof(short)),
            (short)Math.Clamp(
                Math.Round(sample),
                short.MinValue,
                short.MaxValue
            )
        );

    private static void ObserveCancellation(
        int index,
        CancellationToken cancellationToken
    )
    {
        if ((index & 0x7FFF) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
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
