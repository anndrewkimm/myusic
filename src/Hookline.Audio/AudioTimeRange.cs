namespace Hookline.Audio;

public sealed record AudioTimeRange
{
    public AudioTimeRange(TimeSpan start, TimeSpan end)
    {
        if (start < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        Start = start;
        End = end;
    }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public TimeSpan Duration => End - Start;

    public bool Overlaps(TimeSpan start, TimeSpan end) =>
        start < End && end > Start;
}
