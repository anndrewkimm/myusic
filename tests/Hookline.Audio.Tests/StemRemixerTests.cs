using System.Buffers.Binary;

namespace Hookline.Audio.Tests;

public sealed class StemRemixerTests
{
    [Fact]
    public void NaturalGainsReconstructSyntheticSource()
    {
        var format = new PcmAudioFormat(44_100, 16, 2);
        var drums = CreateSnapshot(
            format,
            [1000, -1000, 2000, -2000]
        );
        var vocals = CreateSnapshot(
            format,
            [500, 500, -500, -500]
        );
        var source = CreateSnapshot(
            format,
            [1500, -500, 1500, -2500]
        );
        var stemSet = new SeparatedStemSet
        {
            Mode = StemSeparationMode.FourStem,
            Source = source,
            Stems =
            [
                new SeparatedStem
                {
                    Kind = StemKind.Drums,
                    Snapshot = drums,
                },
                new SeparatedStem
                {
                    Kind = StemKind.Vocals,
                    Snapshot = vocals,
                },
            ],
        };

        var mixed = StemRemixer.Mix(
            stemSet,
            new Dictionary<StemKind, double>
            {
                [StemKind.Drums] = 1,
                [StemKind.Vocals] = 1,
            }
        );

        Assert.True(
            source.Audio.Span.SequenceEqual(mixed.Audio.Span)
        );
        Assert.Equal(source.Duration, mixed.Duration);
        Assert.Equal(source.Format, mixed.Format);
    }

    [Fact]
    public void IndependentGainsCanMuteAndBoostStems()
    {
        var format = new PcmAudioFormat(44_100, 16, 2);
        var source = CreateSnapshot(
            format,
            [0, 0, 0, 0]
        );
        var stemSet = new SeparatedStemSet
        {
            Mode = StemSeparationMode.FourStem,
            Source = source,
            Stems =
            [
                new SeparatedStem
                {
                    Kind = StemKind.Bass,
                    Snapshot = CreateSnapshot(
                        format,
                        [1000, -1000, 2000, -2000]
                    ),
                },
                new SeparatedStem
                {
                    Kind = StemKind.Vocals,
                    Snapshot = CreateSnapshot(
                        format,
                        [8000, 8000, 8000, 8000]
                    ),
                },
            ],
        };

        var mixed = StemRemixer.Mix(
            stemSet,
            new Dictionary<StemKind, double>
            {
                [StemKind.Bass] = 1.5,
                [StemKind.Vocals] = 0,
            }
        );

        Assert.Equal(
            [1500, -1500, 3000, -3000],
            ReadSamples(mixed.Audio.Span)
        );
    }

    [Fact]
    public void MixdownClampsInsteadOfOverflowing()
    {
        var format = new PcmAudioFormat(44_100, 16, 2);
        var source = CreateSnapshot(format, [0, 0]);
        var stemSet = new SeparatedStemSet
        {
            Mode = StemSeparationMode.FourStem,
            Source = source,
            Stems =
            [
                new SeparatedStem
                {
                    Kind = StemKind.Drums,
                    Snapshot = CreateSnapshot(
                        format,
                        [30_000, -30_000]
                    ),
                },
                new SeparatedStem
                {
                    Kind = StemKind.Bass,
                    Snapshot = CreateSnapshot(
                        format,
                        [30_000, -30_000]
                    ),
                },
            ],
        };

        var mixed = StemRemixer.Mix(
            stemSet,
            new Dictionary<StemKind, double>()
        );

        Assert.Equal(
            [short.MaxValue, short.MinValue],
            ReadSamples(mixed.Audio.Span)
        );
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
            TrackInstanceId = 7,
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
