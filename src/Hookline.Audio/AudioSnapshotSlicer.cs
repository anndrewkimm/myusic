namespace Hookline.Audio;

public static class AudioSnapshotSlicer
{
    public static int GetSliceAudioByteCount(
        AudioBufferSnapshot source,
        TimeSpan start,
        TimeSpan end
    )
    {
        ValidateRange(source, start, end);
        var byteCount = 0;
        foreach (var range in source.IncludedRanges)
        {
            var rangeByteCount = source.Format.GetAlignedByteCount(
                range.Duration
            );
            var overlapStart = Max(start, range.Start);
            var overlapEnd = Min(end, range.End);
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            var rangeStartOffset = source.Format.GetAlignedByteCount(
                overlapStart - range.Start
            );
            var rangeEndOffset = Math.Min(
                source.Format.GetAlignedByteCount(
                    overlapEnd - range.Start
                ),
                rangeByteCount
            );
            byteCount = checked(
                byteCount
                    + Math.Max(0, rangeEndOffset - rangeStartOffset)
            );
        }

        return byteCount;
    }

    public static AudioBufferSnapshot Slice(
        AudioBufferSnapshot source,
        TimeSpan start,
        TimeSpan end
    )
    {
        ValidateRange(source, start, end);

        using var output = new MemoryStream();
        var included = new List<AudioTimeRange>();
        var sourceByteOffset = 0;
        foreach (var range in source.IncludedRanges)
        {
            var rangeByteCount = source.Format.GetAlignedByteCount(
                range.Duration
            );
            var overlapStart = Max(start, range.Start);
            var overlapEnd = Min(end, range.End);
            if (overlapEnd > overlapStart)
            {
                var rangeStartOffset =
                    source.Format.GetAlignedByteCount(
                        overlapStart - range.Start
                    );
                var rangeEndOffset =
                    source.Format.GetAlignedByteCount(
                        overlapEnd - range.Start
                    );
                rangeEndOffset = Math.Min(
                    rangeEndOffset,
                    rangeByteCount
                );
                var count = rangeEndOffset - rangeStartOffset;
                if (count > 0)
                {
                    output.Write(
                        source.Audio.Span.Slice(
                            sourceByteOffset + rangeStartOffset,
                            count
                        )
                    );
                    included.Add(
                        new AudioTimeRange(
                            range.Start
                                + source.Format.GetDuration(
                                    rangeStartOffset
                                ),
                            range.Start
                                + source.Format.GetDuration(
                                    rangeEndOffset
                                )
                        )
                    );
                }
            }

            sourceByteOffset += rangeByteCount;
        }

        var excluded = FindExcludedRanges(included, start, end);
        return new AudioBufferSnapshot
        {
            TrackInstanceId = source.TrackInstanceId,
            Format = source.Format,
            Audio = output.ToArray(),
            RequestedStart = start,
            RequestedEnd = end,
            AvailableStart = included.Count == 0
                ? null
                : included[0].Start,
            AvailableEnd = included.Count == 0
                ? null
                : included[^1].End,
            IsStartTruncated =
                included.Count == 0 || included[0].Start > start,
            IsEndTruncated =
                included.Count == 0 || included[^1].End < end,
            HasGaps = excluded.Count > 0,
            IncludedRanges = included,
            ExcludedRanges = excluded,
        };
    }

    public static TimeSpan MapAudioOffsetToTimeline(
        AudioBufferSnapshot snapshot,
        TimeSpan audioOffset
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (audioOffset <= TimeSpan.Zero)
        {
            return snapshot.IncludedRanges.Count == 0
                ? snapshot.RequestedStart
                : snapshot.IncludedRanges[0].Start;
        }

        var remaining = audioOffset;
        foreach (var range in snapshot.IncludedRanges)
        {
            if (remaining <= range.Duration)
            {
                return range.Start + remaining;
            }

            remaining -= range.Duration;
        }

        return snapshot.IncludedRanges.Count == 0
            ? snapshot.RequestedEnd
            : snapshot.IncludedRanges[^1].End;
    }

    private static IReadOnlyList<AudioTimeRange> FindExcludedRanges(
        IReadOnlyList<AudioTimeRange> included,
        TimeSpan start,
        TimeSpan end
    )
    {
        var excluded = new List<AudioTimeRange>();
        var cursor = start;
        foreach (var range in included)
        {
            if (range.Start > cursor)
            {
                excluded.Add(new AudioTimeRange(cursor, range.Start));
            }

            cursor = Max(cursor, range.End);
        }

        if (cursor < end)
        {
            excluded.Add(new AudioTimeRange(cursor, end));
        }

        return excluded;
    }

    private static void ValidateRange(
        AudioBufferSnapshot source,
        TimeSpan start,
        TimeSpan end
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        if (start < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (end <= start)
        {
            throw new ArgumentException(
                "The slice end must follow its start.",
                nameof(end)
            );
        }
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;
}
