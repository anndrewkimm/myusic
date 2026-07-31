using System.Buffers.Binary;

namespace Hookline.Audio.Tests;

public sealed class SegmentedClipRendererTests
{
    [Fact]
    public void WholeNeutralSegmentReturnsOriginalSnapshotUnchanged()
    {
        var source = CreateSnapshot(2, 1_000);
        var settings = new ClipSegmentRenderSettings
        {
            Start = TimeSpan.Zero,
            End = TimeSpan.FromSeconds(2),
            Effects = new ClipEffectSettings(),
        };

        var rendered = SegmentedClipRenderer.Render(source, [settings]);

        Assert.Same(source, rendered);
    }

    [Fact]
    public void IndependentEffectsContributeTheirFullPostEffectDurations()
    {
        var source = CreateSnapshot(2, 1_000);
        ClipSegmentRenderSettings[] segments =
        [
            new()
            {
                Start = TimeSpan.Zero,
                End = TimeSpan.FromSeconds(1),
                Effects = new ClipEffectSettings
                {
                    SpeedMultiplier = 2,
                },
            },
            new()
            {
                Start = TimeSpan.FromSeconds(1),
                End = TimeSpan.FromSeconds(2),
                Effects = new ClipEffectSettings { LoopCount = 2 },
            },
        ];

        var rendered = SegmentedClipRenderer.Render(source, segments);
        var planned = SegmentedClipRenderer.GetOutputDuration(
            source,
            segments
        );

        Assert.Equal(TimeSpan.FromMilliseconds(2_495), planned);
        Assert.Equal(planned, rendered.Duration);
    }

    [Fact]
    public void InternalBoundaryUsesFifteenMillisecondFadeWithoutLosingTime()
    {
        const int sampleRate = 1_000;
        var source = CreateSnapshot(2, sampleRate, 10_000);
        ClipSegmentRenderSettings[] segments =
        [
            new()
            {
                Start = TimeSpan.Zero,
                End = TimeSpan.FromSeconds(1),
                Effects = new ClipEffectSettings(),
            },
            new()
            {
                Start = TimeSpan.FromSeconds(1),
                End = TimeSpan.FromSeconds(2),
                Effects = new ClipEffectSettings(),
            },
        ];

        var rendered = SegmentedClipRenderer.Render(source, segments);
        var samples = ReadSamples(rendered.Audio.Span);

        Assert.Equal(source.Duration, rendered.Duration);
        Assert.Equal(10_000, samples[984]);
        Assert.Equal(0, samples[999]);
        Assert.Equal(0, samples[1_000]);
        Assert.Equal(10_000, samples[1_015]);
    }

    [Fact]
    public void SharedSeparatedStemsCanBeMixedOnlyForTunedSegments()
    {
        var source = CreateSnapshot(2, 1_000, 1_000);
        var vocals = source with
        {
            Audio = CreatePcm(source.Format, 2, 700),
        };
        var other = source with
        {
            Audio = CreatePcm(source.Format, 2, 300),
        };
        var stemSet = new SeparatedStemSet
        {
            Mode = StemSeparationMode.FourStem,
            Source = source,
            Stems =
            [
                new SeparatedStem
                {
                    Kind = StemKind.Vocals,
                    Snapshot = vocals,
                },
                new SeparatedStem
                {
                    Kind = StemKind.Other,
                    Snapshot = other,
                },
            ],
        };
        ClipSegmentRenderSettings[] segments =
        [
            new()
            {
                Start = TimeSpan.Zero,
                End = TimeSpan.FromSeconds(1),
                Effects = new ClipEffectSettings(),
                StemGains = new Dictionary<StemKind, double>
                {
                    [StemKind.Vocals] = 0,
                    [StemKind.Other] = 1,
                },
            },
            new()
            {
                Start = TimeSpan.FromSeconds(1),
                End = TimeSpan.FromSeconds(2),
                Effects = new ClipEffectSettings(),
            },
        ];

        var rendered = SegmentedClipRenderer.Render(
            source,
            segments,
            stemSet
        );
        var samples = ReadSamples(rendered.Audio.Span);

        Assert.Equal(300, samples[100]);
        Assert.Equal(1_000, samples[1_100]);
    }

    private static AudioBufferSnapshot CreateSnapshot(
        int seconds,
        int sampleRate,
        short sample = 1_000
    )
    {
        var format = new PcmAudioFormat(sampleRate, 16, 1);
        var duration = TimeSpan.FromSeconds(seconds);
        return new AudioBufferSnapshot
        {
            TrackInstanceId = 15,
            Format = format,
            Audio = CreatePcm(format, seconds, sample),
            RequestedStart = TimeSpan.Zero,
            RequestedEnd = duration,
            AvailableStart = TimeSpan.Zero,
            AvailableEnd = duration,
            IncludedRanges = [new AudioTimeRange(TimeSpan.Zero, duration)],
        };
    }

    private static byte[] CreatePcm(
        PcmAudioFormat format,
        int seconds,
        short sample
    )
    {
        var output = new byte[
            seconds * format.SampleRate * format.BlockAlign
        ];
        for (var offset = 0; offset < output.Length; offset += sizeof(short))
        {
            BinaryPrimitives.WriteInt16LittleEndian(
                output.AsSpan(offset, sizeof(short)),
                sample
            );
        }

        return output;
    }

    private static short[] ReadSamples(ReadOnlySpan<byte> audio)
    {
        var samples = new short[audio.Length / sizeof(short)];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = BinaryPrimitives.ReadInt16LittleEndian(
                audio.Slice(index * sizeof(short), sizeof(short))
            );
        }

        return samples;
    }
}
