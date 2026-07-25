namespace Hookline.Audio;

public sealed record PcmAudioFormat
{
    public PcmAudioFormat(
        int sampleRate,
        short bitsPerSample,
        short channels
    )
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (bitsPerSample <= 0 || bitsPerSample % 8 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample));
        }

        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        SampleRate = sampleRate;
        BitsPerSample = bitsPerSample;
        Channels = channels;
    }

    public int SampleRate { get; }

    public short BitsPerSample { get; }

    public short Channels { get; }

    public int BlockAlign => Channels * (BitsPerSample / 8);

    public int AverageBytesPerSecond => checked(SampleRate * BlockAlign);

    public TimeSpan GetDuration(long byteCount)
    {
        var alignedByteCount = byteCount - (byteCount % BlockAlign);
        return TimeSpan.FromSeconds(
            alignedByteCount / (double)AverageBytesPerSecond
        );
    }

    public int GetAlignedByteCount(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return 0;
        }

        var frames = (long)Math.Floor(
            duration.TotalSeconds * SampleRate
        );
        return checked((int)(frames * BlockAlign));
    }
}
