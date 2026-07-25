namespace Hookline.Audio;

/// <summary>
/// Stores immutable PCM chunks in capture order under a strict byte bound.
/// </summary>
public sealed class RollingAudioBuffer
{
    private readonly object _gate = new();
    private readonly List<AudioChunk> _chunks = [];
    private readonly long _maximumBytes;

    private long _nextSequence;
    private long _totalBytes;

    public RollingAudioBuffer(
        PcmAudioFormat format,
        TimeSpan window
    )
    {
        Format = format ?? throw new ArgumentNullException(nameof(format));
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        Window = window;
        var rawMaximum = checked(
            (long)Math.Ceiling(
                window.TotalSeconds * format.AverageBytesPerSecond
            )
        );
        _maximumBytes =
            rawMaximum - (rawMaximum % format.BlockAlign);
        if (_maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }
    }

    public PcmAudioFormat Format { get; }

    public TimeSpan Window { get; }

    public long BufferedBytes
    {
        get
        {
            lock (_gate)
            {
                return _totalBytes;
            }
        }
    }

    public int ChunkCount
    {
        get
        {
            lock (_gate)
            {
                return _chunks.Count;
            }
        }
    }

    public void Append(
        long trackInstanceId,
        TimeSpan playbackStart,
        ReadOnlySpan<byte> audio
    )
    {
        if (trackInstanceId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trackInstanceId)
            );
        }

        if (playbackStart < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playbackStart)
            );
        }

        var alignedLength =
            audio.Length - (audio.Length % Format.BlockAlign);
        if (alignedLength == 0)
        {
            return;
        }

        var bytesToSkip = 0;
        if (alignedLength > _maximumBytes)
        {
            bytesToSkip = checked(
                alignedLength - (int)_maximumBytes
            );
            bytesToSkip -= bytesToSkip % Format.BlockAlign;
            alignedLength -= bytesToSkip;
            playbackStart += Format.GetDuration(bytesToSkip);
        }

        var copy = audio
            .Slice(bytesToSkip, alignedLength)
            .ToArray();
        var chunk = new AudioChunk(
            Interlocked.Increment(ref _nextSequence),
            trackInstanceId,
            playbackStart,
            playbackStart + Format.GetDuration(copy.Length),
            copy
        );

        lock (_gate)
        {
            _chunks.Add(chunk);
            _totalBytes += copy.Length;
            EvictOldestBytes();
        }
    }

    public AudioBufferSnapshot Query(
        long trackInstanceId,
        TimeSpan? start = null,
        TimeSpan? end = null
    )
    {
        if (trackInstanceId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trackInstanceId)
            );
        }

        if (start < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (end < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        AudioChunk[] chunks;
        lock (_gate)
        {
            chunks = _chunks
                .Where(
                    chunk =>
                        chunk.TrackInstanceId == trackInstanceId
                )
                .OrderBy(chunk => chunk.Sequence)
                .ToArray();
        }

        if (chunks.Length == 0)
        {
            var emptyStart = start ?? TimeSpan.Zero;
            var emptyEnd = end ?? emptyStart;
            if (emptyEnd < emptyStart)
            {
                throw new ArgumentException(
                    "The query end must not precede its start."
                );
            }

            return new AudioBufferSnapshot
            {
                TrackInstanceId = trackInstanceId,
                Format = Format,
                Audio = ReadOnlyMemory<byte>.Empty,
                RequestedStart = emptyStart,
                RequestedEnd = emptyEnd,
                IsStartTruncated = start.HasValue,
                IsEndTruncated = end.HasValue,
            };
        }

        var availableStart = chunks.Min(chunk => chunk.PlaybackStart);
        var availableEnd = chunks.Max(chunk => chunk.PlaybackEnd);
        var requestedStart = start ?? availableStart;
        var requestedEnd = end ?? availableEnd;
        if (requestedEnd < requestedStart)
        {
            throw new ArgumentException(
                "The query end must not precede its start."
            );
        }

        var selectedRanges = new List<SelectedRange>();
        using var output = new MemoryStream();
        foreach (var chunk in chunks)
        {
            var rangeStart = Max(
                requestedStart,
                chunk.PlaybackStart
            );
            var rangeEnd = Min(requestedEnd, chunk.PlaybackEnd);
            if (rangeEnd <= rangeStart)
            {
                continue;
            }

            var startOffset = Format.GetAlignedByteCount(
                rangeStart - chunk.PlaybackStart
            );
            var endOffset = Format.GetAlignedByteCount(
                rangeEnd - chunk.PlaybackStart
            );
            endOffset = Math.Min(endOffset, chunk.Audio.Length);
            if (endOffset <= startOffset)
            {
                continue;
            }

            output.Write(
                chunk.Audio,
                startOffset,
                endOffset - startOffset
            );
            selectedRanges.Add(
                new SelectedRange(
                    chunk.PlaybackStart
                        + Format.GetDuration(startOffset),
                    chunk.PlaybackStart
                        + Format.GetDuration(endOffset)
                )
            );
        }

        var hasGaps = false;
        for (var index = 1; index < selectedRanges.Count; index++)
        {
            if (
                selectedRanges[index].Start
                > selectedRanges[index - 1].End
                    + TimeSpan.FromSeconds(
                        1d / Format.SampleRate
                    )
            )
            {
                hasGaps = true;
                break;
            }
        }

        return new AudioBufferSnapshot
        {
            TrackInstanceId = trackInstanceId,
            Format = Format,
            Audio = output.ToArray(),
            RequestedStart = requestedStart,
            RequestedEnd = requestedEnd,
            AvailableStart = availableStart,
            AvailableEnd = availableEnd,
            IsStartTruncated = requestedStart < availableStart,
            IsEndTruncated = requestedEnd > availableEnd,
            HasGaps = hasGaps,
        };
    }

    public AudioBufferSnapshot QueryRecent(
        long trackInstanceId,
        TimeSpan duration
    )
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var allAvailable = Query(trackInstanceId);
        if (allAvailable.AvailableEnd is null)
        {
            return allAvailable;
        }

        var start = allAvailable.AvailableEnd.Value - duration;
        if (start < TimeSpan.Zero)
        {
            start = TimeSpan.Zero;
        }

        return Query(
            trackInstanceId,
            start,
            allAvailable.AvailableEnd
        );
    }

    private void EvictOldestBytes()
    {
        while (_totalBytes > _maximumBytes && _chunks.Count > 0)
        {
            var excess = _totalBytes - _maximumBytes;
            var oldest = _chunks[0];
            if (oldest.Audio.Length <= excess)
            {
                _chunks.RemoveAt(0);
                _totalBytes -= oldest.Audio.Length;
                continue;
            }

            var bytesToRemove = checked((int)excess);
            var remainder = bytesToRemove % Format.BlockAlign;
            if (remainder != 0)
            {
                bytesToRemove += Format.BlockAlign - remainder;
            }

            var remaining = oldest.Audio
                .AsSpan(bytesToRemove)
                .ToArray();
            _chunks[0] = oldest with
            {
                PlaybackStart =
                    oldest.PlaybackStart
                    + Format.GetDuration(bytesToRemove),
                Audio = remaining,
            };
            _totalBytes -= bytesToRemove;
        }
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;

    private sealed record AudioChunk(
        long Sequence,
        long TrackInstanceId,
        TimeSpan PlaybackStart,
        TimeSpan PlaybackEnd,
        byte[] Audio
    );

    private sealed record SelectedRange(
        TimeSpan Start,
        TimeSpan End
    );
}
