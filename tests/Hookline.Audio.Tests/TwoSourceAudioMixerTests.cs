using System.Buffers.Binary;

namespace Hookline.Audio.Tests;

public sealed class TwoSourceAudioMixerTests
{
    [Fact]
    public void LongerSourceSetsDurationAndShorterSourceLoops()
    {
        var format = new PcmAudioFormat(1_000, 16, 1);
        var shorter = CreateSnapshot(
            format,
            Enumerable.Repeat((short)1_000, 10).ToArray()
        );
        var longer = CreateSnapshot(
            format,
            Enumerable.Repeat((short)2_000, 30).ToArray()
        );

        var mixed = TwoSourceAudioMixer.Mix(
            shorter,
            0.5,
            longer,
            1
        );

        Assert.Equal(longer.Duration, mixed.Duration);
        Assert.Equal(
            TwoSourceAudioMixer.MixedTrackInstanceId,
            mixed.TrackInstanceId
        );
        Assert.All(
            ReadSamples(mixed.Audio.Span),
            sample => Assert.Equal(2_500, sample)
        );
    }

    [Fact]
    public void IndependentGainsAllowMuteBoostAndSelfMix()
    {
        var format = new PcmAudioFormat(44_100, 16, 2);
        var source = CreateSnapshot(
            format,
            [1_000, -1_000, 2_000, -2_000]
        );

        var mixed = TwoSourceAudioMixer.Mix(
            source,
            0,
            source,
            1.5
        );

        Assert.Equal(
            [1_500, -1_500, 3_000, -3_000],
            ReadSamples(mixed.Audio.Span)
        );
    }

    [Fact]
    public void MixdownClampsInsteadOfOverflowing()
    {
        var format = new PcmAudioFormat(44_100, 16, 1);
        var source = CreateSnapshot(
            format,
            [30_000, -30_000]
        );

        var mixed = TwoSourceAudioMixer.Mix(
            source,
            1.5,
            source,
            1.5
        );

        Assert.Equal(
            [short.MaxValue, short.MinValue],
            ReadSamples(mixed.Audio.Span)
        );
    }

    [Fact]
    public void MismatchedFormatsAreRejected()
    {
        var first = CreateSnapshot(
            new PcmAudioFormat(44_100, 16, 2),
            [1, 1]
        );
        var second = CreateSnapshot(
            new PcmAudioFormat(48_000, 16, 2),
            [1, 1]
        );

        Assert.Throws<ArgumentException>(
            () => TwoSourceAudioMixer.Mix(first, 1, second, 1)
        );
    }

    [Fact]
    public void OutputBeyondEffectsLimitIsRejectedClearly()
    {
        var format = new PcmAudioFormat(10, 16, 1);
        var tooLong = CreateSnapshot(
            format,
            new short[3_001]
        );
        var shortSource = CreateSnapshot(format, [1]);

        var exception = Assert.Throws<InvalidOperationException>(
            () =>
                TwoSourceAudioMixer.Mix(
                    tooLong,
                    1,
                    shortSource,
                    1
                )
        );

        Assert.Contains("5 minutes", exception.Message);
    }

    private static AudioBufferSnapshot CreateSnapshot(
        PcmAudioFormat format,
        IReadOnlyList<short> samples
    )
    {
        var audio = new byte[samples.Count * sizeof(short)];
        for (var index = 0; index < samples.Count; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(
                audio.AsSpan(
                    index * sizeof(short),
                    sizeof(short)
                ),
                samples[index]
            );
        }

        var duration = format.GetDuration(audio.Length);
        return new AudioBufferSnapshot
        {
            TrackInstanceId = 12,
            Format = format,
            Audio = audio,
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

    private static short[] ReadSamples(ReadOnlySpan<byte> audio)
    {
        var samples = new short[audio.Length / sizeof(short)];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] =
                BinaryPrimitives.ReadInt16LittleEndian(
                    audio.Slice(
                        index * sizeof(short),
                        sizeof(short)
                    )
                );
        }

        return samples;
    }
}
