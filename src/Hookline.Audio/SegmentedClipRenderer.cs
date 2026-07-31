using System.Buffers.Binary;

namespace Hookline.Audio;

public static class SegmentedClipRenderer
{
    public static AudioBufferSnapshot Render(
        AudioBufferSnapshot source,
        IReadOnlyList<ClipSegmentRenderSettings> segments,
        SeparatedStemSet? separatedStems = null,
        CancellationToken cancellationToken = default
    )
    {
        Validate(source, segments);
        if (segments.Count == 1 && IsWholeNeutralSegment(source, segments[0]))
        {
            return source;
        }

        var renderedSegments = new AudioBufferSnapshot[segments.Count];
        for (var index = 0; index < segments.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segment = segments[index];
            var segmentSource = AudioSnapshotSlicer.Slice(
                source,
                segment.Start,
                segment.End
            );
            if (HasCustomStemMix(segment.StemGains))
            {
                if (separatedStems is null)
                {
                    throw new InvalidOperationException(
                        AudioStrings.SegmentedStemMixUnavailable
                    );
                }

                segmentSource = StemRemixer.Mix(
                    SliceStemSet(
                        separatedStems,
                        segment.Start,
                        segment.End
                    ),
                    segment.StemGains,
                    cancellationToken
                );
            }

            renderedSegments[index] = ClipEffectsProcessor.Process(
                segmentSource,
                segment.Effects,
                cancellationToken
            );
        }

        return Stitch(source, renderedSegments, cancellationToken);
    }

    public static TimeSpan GetOutputDuration(
        AudioBufferSnapshot source,
        IReadOnlyList<ClipSegmentRenderSettings> segments
    )
    {
        Validate(source, segments);
        long totalTicks = 0;
        foreach (var segment in segments)
        {
            var byteCount = AudioSnapshotSlicer.GetSliceAudioByteCount(
                source,
                segment.Start,
                segment.End
            );
            var slice = source with
            {
                Audio = source.Audio[..byteCount],
                RequestedStart = segment.Start,
                RequestedEnd = segment.End,
            };
            var duration = ClipEffectsProcessor.GetOutputDuration(
                slice,
                segment.Effects
            );
            totalTicks = checked(totalTicks + duration.Ticks);
        }

        return TimeSpan.FromTicks(totalTicks);
    }

    private static AudioBufferSnapshot Stitch(
        AudioBufferSnapshot source,
        IReadOnlyList<AudioBufferSnapshot> segments,
        CancellationToken cancellationToken
    )
    {
        var format = source.Format;
        var totalByteCount = segments.Aggregate(
            0L,
            (total, segment) => checked(total + segment.Audio.Length)
        );
        if (totalByteCount > Array.MaxLength)
        {
            throw new InvalidOperationException(
                AudioStrings.SegmentedOutputTooLarge
            );
        }

        var output = new byte[(int)totalByteCount];
        var segmentStarts = new int[segments.Count];
        var offset = 0;
        for (var index = 0; index < segments.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            segmentStarts[index] = offset;
            segments[index].Audio.Span.CopyTo(output.AsSpan(offset));
            offset += segments[index].Audio.Length;
        }

        for (var index = 1; index < segments.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyBoundaryFade(
                output,
                format,
                segmentStarts[index],
                segments[index - 1].Audio.Length,
                segments[index].Audio.Length
            );
        }

        var start = source.RequestedStart;
        var end = start + format.GetDuration(output.Length);
        return source with
        {
            Audio = output,
            RequestedStart = start,
            RequestedEnd = end,
            AvailableStart = output.Length == 0 ? null : start,
            AvailableEnd = output.Length == 0 ? null : end,
            IsStartTruncated = output.Length == 0,
            IsEndTruncated = output.Length == 0,
            HasGaps = false,
            IncludedRanges =
                output.Length == 0
                    ? Array.Empty<AudioTimeRange>()
                    : [new AudioTimeRange(start, end)],
            ExcludedRanges = Array.Empty<AudioTimeRange>(),
        };
    }

    private static void ApplyBoundaryFade(
        Span<byte> audio,
        PcmAudioFormat format,
        int boundaryByteOffset,
        int leftByteCount,
        int rightByteCount
    )
    {
        var desiredFrames = Math.Max(
            1,
            (int)Math.Round(
                ClipFadeSettings.Duration.TotalSeconds
                    * format.SampleRate
            )
        );
        var leftFrames = leftByteCount / format.BlockAlign;
        var rightFrames = rightByteCount / format.BlockAlign;
        var fadeFrames = Math.Min(
            desiredFrames,
            Math.Min(leftFrames, rightFrames)
        );
        for (var frame = 0; frame < fadeFrames; frame++)
        {
            var fadeIn = frame / (double)fadeFrames;
            var fadeOut = (fadeFrames - frame - 1d) / fadeFrames;
            ScaleFrame(
                audio,
                boundaryByteOffset / format.BlockAlign
                    - fadeFrames
                    + frame,
                format,
                fadeOut
            );
            ScaleFrame(
                audio,
                boundaryByteOffset / format.BlockAlign + frame,
                format,
                fadeIn
            );
        }
    }

    private static void ScaleFrame(
        Span<byte> audio,
        int frame,
        PcmAudioFormat format,
        double multiplier
    )
    {
        for (var channel = 0; channel < format.Channels; channel++)
        {
            var offset =
                (frame * format.BlockAlign)
                + (channel * sizeof(short));
            var sample = BinaryPrimitives.ReadInt16LittleEndian(
                audio.Slice(offset, sizeof(short))
            );
            BinaryPrimitives.WriteInt16LittleEndian(
                audio.Slice(offset, sizeof(short)),
                (short)Math.Clamp(
                    Math.Round(sample * multiplier),
                    short.MinValue,
                    short.MaxValue
                )
            );
        }
    }

    private static SeparatedStemSet SliceStemSet(
        SeparatedStemSet stemSet,
        TimeSpan start,
        TimeSpan end
    ) =>
        stemSet with
        {
            Source = AudioSnapshotSlicer.Slice(
                stemSet.Source,
                start,
                end
            ),
            Stems = stemSet.Stems
                .Select(
                    stem =>
                        stem with
                        {
                            Snapshot = AudioSnapshotSlicer.Slice(
                                stem.Snapshot,
                                start,
                                end
                            ),
                        }
                )
                .ToArray(),
        };

    private static bool HasCustomStemMix(
        IReadOnlyDictionary<StemKind, double> gains
    ) => gains.Any(pair => pair.Value != 1d);

    private static bool IsWholeNeutralSegment(
        AudioBufferSnapshot source,
        ClipSegmentRenderSettings segment
    ) =>
        segment.Start == source.RequestedStart
        && segment.End == source.RequestedEnd
        && segment.Effects.IsNeutral
        && !HasCustomStemMix(segment.StemGains);

    private static void Validate(
        AudioBufferSnapshot source,
        IReadOnlyList<ClipSegmentRenderSettings> segments
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0)
        {
            throw new ArgumentException(
                AudioStrings.SegmentedSelectionEmpty,
                nameof(segments)
            );
        }

        var expectedStart = source.RequestedStart;
        foreach (var segment in segments)
        {
            ArgumentNullException.ThrowIfNull(segment);
            ArgumentNullException.ThrowIfNull(segment.Effects);
            ArgumentNullException.ThrowIfNull(segment.StemGains);
            if (
                segment.Start != expectedStart
                || segment.End <= segment.Start
                || segment.End > source.RequestedEnd
            )
            {
                throw new ArgumentException(
                    AudioStrings.InvalidSegmentTimeline,
                    nameof(segments)
                );
            }

            expectedStart = segment.End;
        }

        if (expectedStart != source.RequestedEnd)
        {
            throw new ArgumentException(
                AudioStrings.InvalidSegmentTimeline,
                nameof(segments)
            );
        }
    }
}
