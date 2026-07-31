namespace Hookline.Audio.Tests;

public sealed class AudioSnapshotSlicerTests
{
    private static readonly PcmAudioFormat Format =
        new(sampleRate: 100, bitsPerSample: 16, channels: 1);

    [Fact]
    public void SlicePreservesTimelineGapsAndOnlyCopiesIncludedAudio()
    {
        var buffer = new RollingAudioBuffer(
            Format,
            TimeSpan.FromSeconds(10)
        );
        buffer.Append(1, TimeSpan.Zero, Repeated(1, 100));
        buffer.Append(
            1,
            TimeSpan.FromSeconds(1),
            Repeated(2, 100)
        );

        var source = buffer.Query(1);
        var slice = AudioSnapshotSlicer.Slice(
            source,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(1_250)
        );
        var plannedByteCount =
            AudioSnapshotSlicer.GetSliceAudioByteCount(
                source,
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(1_250)
            );

        Assert.Equal(100, slice.Audio.Length);
        Assert.Equal(slice.Audio.Length, plannedByteCount);
        Assert.Equal(2, slice.IncludedRanges.Count);
        Assert.Equal(
            new AudioTimeRange(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(500)
            ),
            slice.IncludedRanges[0]
        );
        Assert.Equal(
            new AudioTimeRange(
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromSeconds(1)
            ),
            Assert.Single(slice.ExcludedRanges)
        );
        Assert.True(slice.HasGaps);
    }

    [Fact]
    public void AudioOffsetMapsAcrossAnExcludedTimelineSpan()
    {
        var snapshot = new AudioBufferSnapshot
        {
            TrackInstanceId = 1,
            Format = Format,
            Audio = new byte[200],
            RequestedStart = TimeSpan.Zero,
            RequestedEnd = TimeSpan.FromSeconds(2),
            AvailableStart = TimeSpan.Zero,
            AvailableEnd = TimeSpan.FromSeconds(2),
            HasGaps = true,
            IncludedRanges =
            [
                new AudioTimeRange(
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(500)
                ),
                new AudioTimeRange(
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(1.5)
                ),
            ],
            ExcludedRanges =
            [
                new AudioTimeRange(
                    TimeSpan.FromMilliseconds(500),
                    TimeSpan.FromSeconds(1)
                ),
            ],
        };

        var mapped = AudioSnapshotSlicer.MapAudioOffsetToTimeline(
            snapshot,
            TimeSpan.FromMilliseconds(750)
        );

        Assert.Equal(TimeSpan.FromSeconds(1.25), mapped);
    }

    private static byte[] Repeated(byte value, int count) =>
        Enumerable.Repeat(value, count).ToArray();
}
