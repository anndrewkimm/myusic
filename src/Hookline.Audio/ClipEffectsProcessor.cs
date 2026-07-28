using System.Buffers.Binary;
using NAudio.Dsp;

namespace Hookline.Audio;

public static class ClipEffectsProcessor
{
    private const float EqualizerBandQ = 1.4f;
    private static readonly TimeSpan LoopCrossfadeDuration =
        TimeSpan.FromMilliseconds(5);

    public static AudioBufferSnapshot Process(
        AudioBufferSnapshot source,
        ClipEffectSettings settings,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        Validate(source, settings);

        if (settings.IsNeutral || source.Audio.IsEmpty)
        {
            return source;
        }

        var plan = CreatePlan(source, settings);
        var audio = source.Audio;
        if (settings.SpeedMultiplier != 1)
        {
            audio = ChangeSpeed(
                audio.Span,
                source.Format,
                settings.SpeedMultiplier,
                plan.SpeedFrameCount,
                cancellationToken
            );
        }

        if (!settings.EqualizerCurve.IsFlat)
        {
            audio = ApplyEqualizer(
                audio.Span,
                source.Format,
                settings.EqualizerCurve,
                cancellationToken
            );
        }

        if (settings.LoopCount > 1)
        {
            audio = AssembleLoops(
                audio.Span,
                source.Format,
                settings.LoopCount,
                plan.FinalFrameCount,
                plan.LoopCrossfadeFrameCount,
                cancellationToken
            );
        }

        return CreateProcessedSnapshot(source, audio);
    }

    public static TimeSpan GetOutputDuration(
        AudioBufferSnapshot source,
        ClipEffectSettings settings
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        Validate(source, settings);
        if (settings.IsNeutral)
        {
            return source.Duration;
        }

        var plan = CreatePlan(source, settings);
        return source.Format.GetDuration(
            (long)plan.FinalFrameCount
                * source.Format.BlockAlign
        );
    }

    private static ProcessingPlan CreatePlan(
        AudioBufferSnapshot source,
        ClipEffectSettings settings
    )
    {
        var format = source.Format;
        var sourceFrameCount =
            source.Audio.Length / format.BlockAlign;
        if (sourceFrameCount == 0)
        {
            return new ProcessingPlan(0, 0, 0);
        }

        var durationCapFrames = (long)Math.Floor(
            ClipEffectSettings.MaximumExpandedDuration.TotalSeconds
                * format.SampleRate
        );
        var arrayCapFrames =
            Array.MaxLength / format.BlockAlign;
        var expansionCapFrames = Math.Min(
            arrayCapFrames,
            Math.Max(sourceFrameCount, durationCapFrames)
        );

        var desiredSpeedFrames = Math.Max(
            1L,
            (long)Math.Round(
                sourceFrameCount / settings.SpeedMultiplier,
                MidpointRounding.AwayFromZero
            )
        );
        var speedFrameCount = (int)Math.Min(
            desiredSpeedFrames,
            expansionCapFrames
        );
        var crossfadeFrameCount =
            settings.LoopCount == 1
                ? 0
                : GetLoopCrossfadeFrameCount(
                    speedFrameCount,
                    format.SampleRate
                );
        var framesPerAdditionalLoop =
            speedFrameCount - crossfadeFrameCount;
        long desiredFinalFrames = speedFrameCount;
        for (
            var repetition = 1;
            repetition < settings.LoopCount
                && desiredFinalFrames < expansionCapFrames;
            repetition++
        )
        {
            desiredFinalFrames = Math.Min(
                expansionCapFrames,
                desiredFinalFrames
                    + framesPerAdditionalLoop
            );
        }

        return new ProcessingPlan(
            speedFrameCount,
            (int)desiredFinalFrames,
            crossfadeFrameCount
        );
    }

    private static byte[] ChangeSpeed(
        ReadOnlySpan<byte> source,
        PcmAudioFormat format,
        double speedMultiplier,
        int outputFrameCount,
        CancellationToken cancellationToken
    )
    {
        var sourceFrameCount = source.Length / format.BlockAlign;
        var output = new byte[
            checked(outputFrameCount * format.BlockAlign)
        ];
        for (
            var outputFrame = 0;
            outputFrame < outputFrameCount;
            outputFrame++
        )
        {
            if ((outputFrame & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var sourcePosition = Math.Min(
                outputFrame * speedMultiplier,
                sourceFrameCount - 1d
            );
            var firstFrame = (int)sourcePosition;
            var secondFrame = Math.Min(
                firstFrame + 1,
                sourceFrameCount - 1
            );
            var fraction = sourcePosition - firstFrame;
            for (
                var channel = 0;
                channel < format.Channels;
                channel++
            )
            {
                var first = ReadSample(
                    source,
                    firstFrame,
                    channel,
                    format
                );
                var second = ReadSample(
                    source,
                    secondFrame,
                    channel,
                    format
                );
                WriteSample(
                    output,
                    outputFrame,
                    channel,
                    format,
                    first + ((second - first) * fraction)
                );
            }
        }

        return output;
    }

    private static byte[] ApplyEqualizer(
        ReadOnlySpan<byte> source,
        PcmAudioFormat format,
        GraphicEqualizerCurve curve,
        CancellationToken cancellationToken
    )
    {
        var output = new byte[
            source.Length - (source.Length % format.BlockAlign)
        ];
        var activeBands = GraphicEqualizerBands
            .CenterFrequencies
            .Select(
                (frequency, index) =>
                    new
                    {
                        Frequency = frequency,
                        Gain = curve[index],
                    }
            )
            .Where(
                band =>
                    band.Gain != 0
                    && band.Frequency < format.SampleRate / 2d
            )
            .ToArray();
        var filters = Enumerable
            .Range(0, format.Channels)
            .Select(
                _ =>
                    activeBands
                        .Select(
                            band =>
                                BiQuadFilter.PeakingEQ(
                                    format.SampleRate,
                                    band.Frequency,
                                    EqualizerBandQ,
                                    (float)band.Gain
                                )
                        )
                        .ToArray()
            )
            .ToArray();
        var frameCount = output.Length / format.BlockAlign;
        for (var frame = 0; frame < frameCount; frame++)
        {
            if ((frame & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (
                var channel = 0;
                channel < format.Channels;
                channel++
            )
            {
                var sample = ReadSample(
                    source,
                    frame,
                    channel,
                    format
                );
                var filtered = sample / 32768f;
                foreach (var filter in filters[channel])
                {
                    filtered = Math.Clamp(
                        filter.Transform(filtered),
                        -1f,
                        short.MaxValue / 32768f
                    );
                }

                WriteSample(
                    output,
                    frame,
                    channel,
                    format,
                    filtered * 32768d
                );
            }
        }

        return output;
    }

    private static byte[] AssembleLoops(
        ReadOnlySpan<byte> source,
        PcmAudioFormat format,
        int loopCount,
        int outputFrameCount,
        int crossfadeFrameCount,
        CancellationToken cancellationToken
    )
    {
        var sourceFrameCount = source.Length / format.BlockAlign;
        var output = new byte[
            checked(outputFrameCount * format.BlockAlign)
        ];
        var writtenFrames = Math.Min(
            sourceFrameCount,
            outputFrameCount
        );
        source[
            ..(writtenFrames * format.BlockAlign)
        ].CopyTo(output);

        for (
            var repetition = 1;
            repetition < loopCount
                && writtenFrames < outputFrameCount;
            repetition++
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var overlapFrames = Math.Min(
                crossfadeFrameCount,
                Math.Min(writtenFrames, sourceFrameCount)
            );
            for (
                var overlapFrame = 0;
                overlapFrame < overlapFrames;
                overlapFrame++
            )
            {
                var outputFrame =
                    writtenFrames
                    - overlapFrames
                    + overlapFrame;
                var incomingWeight =
                    (overlapFrame + 1d)
                    / (overlapFrames + 1d);
                for (
                    var channel = 0;
                    channel < format.Channels;
                    channel++
                )
                {
                    var outgoing = ReadSample(
                        output,
                        outputFrame,
                        channel,
                        format
                    );
                    var incoming = ReadSample(
                        source,
                        overlapFrame,
                        channel,
                        format
                    );
                    WriteSample(
                        output,
                        outputFrame,
                        channel,
                        format,
                        (outgoing * (1 - incomingWeight))
                            + (incoming * incomingWeight)
                    );
                }
            }

            var availableFrames =
                outputFrameCount - writtenFrames;
            var copyFrames = Math.Min(
                sourceFrameCount - overlapFrames,
                availableFrames
            );
            source.Slice(
                overlapFrames * format.BlockAlign,
                copyFrames * format.BlockAlign
            ).CopyTo(
                output.AsSpan(
                    writtenFrames * format.BlockAlign
                )
            );
            writtenFrames += copyFrames;
        }

        return output;
    }

    private static AudioBufferSnapshot CreateProcessedSnapshot(
        AudioBufferSnapshot source,
        ReadOnlyMemory<byte> audio
    )
    {
        var start = source.RequestedStart;
        var end = start + source.Format.GetDuration(audio.Length);
        IReadOnlyList<AudioTimeRange> included =
            audio.IsEmpty
                ? Array.Empty<AudioTimeRange>()
                : [new AudioTimeRange(start, end)];
        return source with
        {
            Audio = audio,
            RequestedEnd = end,
            AvailableStart = audio.IsEmpty ? null : start,
            AvailableEnd = audio.IsEmpty ? null : end,
            IsStartTruncated = audio.IsEmpty,
            IsEndTruncated = audio.IsEmpty,
            HasGaps = false,
            IncludedRanges = included,
            ExcludedRanges = Array.Empty<AudioTimeRange>(),
        };
    }

    private static int GetLoopCrossfadeFrameCount(
        int sourceFrameCount,
        int sampleRate
    )
    {
        if (sourceFrameCount < 2)
        {
            return 0;
        }

        var desiredFrames = Math.Max(
            1,
            (int)Math.Round(
                LoopCrossfadeDuration.TotalSeconds * sampleRate
            )
        );
        return Math.Min(desiredFrames, sourceFrameCount / 2);
    }

    private static short ReadSample(
        ReadOnlySpan<byte> audio,
        int frame,
        int channel,
        PcmAudioFormat format
    )
    {
        var offset =
            (frame * format.BlockAlign)
            + (channel * sizeof(short));
        return BinaryPrimitives.ReadInt16LittleEndian(
            audio.Slice(offset, sizeof(short))
        );
    }

    private static void WriteSample(
        Span<byte> audio,
        int frame,
        int channel,
        PcmAudioFormat format,
        double sample
    )
    {
        var clamped = (short)Math.Clamp(
            Math.Round(sample),
            short.MinValue,
            short.MaxValue
        );
        var offset =
            (frame * format.BlockAlign)
            + (channel * sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(
            audio.Slice(offset, sizeof(short)),
            clamped
        );
    }

    private static void Validate(
        AudioBufferSnapshot source,
        ClipEffectSettings settings
    )
    {
        if (source.Format.BitsPerSample != 16)
        {
            throw new NotSupportedException(
                AudioStrings.UnsupportedEffectsFormat
            );
        }

        if (
            !double.IsFinite(settings.SpeedMultiplier)
            || settings.SpeedMultiplier
                < ClipEffectSettings.MinimumSpeedMultiplier
            || settings.SpeedMultiplier
                > ClipEffectSettings.MaximumSpeedMultiplier
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                AudioStrings.InvalidEffectSpeed
            );
        }

        if (
            settings.LoopCount
                < ClipEffectSettings.MinimumLoopCount
            || settings.LoopCount
                > ClipEffectSettings.MaximumLoopCount
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                AudioStrings.InvalidLoopCount
            );
        }

        ArgumentNullException.ThrowIfNull(settings.EqualizerCurve);
    }

    private sealed record ProcessingPlan(
        int SpeedFrameCount,
        int FinalFrameCount,
        int LoopCrossfadeFrameCount
    );
}
