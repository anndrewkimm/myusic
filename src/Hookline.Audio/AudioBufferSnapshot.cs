namespace Hookline.Audio;

public sealed record AudioBufferSnapshot
{
    public required long TrackInstanceId { get; init; }

    public required PcmAudioFormat Format { get; init; }

    public required ReadOnlyMemory<byte> Audio { get; init; }

    public required TimeSpan RequestedStart { get; init; }

    public required TimeSpan RequestedEnd { get; init; }

    public TimeSpan? AvailableStart { get; init; }

    public TimeSpan? AvailableEnd { get; init; }

    public bool IsStartTruncated { get; init; }

    public bool IsEndTruncated { get; init; }

    public bool HasGaps { get; init; }

    public TimeSpan Duration => Format.GetDuration(Audio.Length);
}
