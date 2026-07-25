namespace Hookline.Audio.Tests;

public sealed class RollingAudioBufferTests
{
    private static readonly PcmAudioFormat Format =
        new(sampleRate: 100, bitsPerSample: 16, channels: 1);

    [Fact]
    public void SustainedMultiHourInputStaysStrictlyBounded()
    {
        var buffer = new RollingAudioBuffer(
            Format,
            TimeSpan.FromSeconds(5)
        );
        var chunk = Enumerable
            .Repeat((byte)7, 20)
            .ToArray();

        for (var index = 0; index < 360_000; index++)
        {
            buffer.Append(
                trackInstanceId: 1,
                playbackStart: TimeSpan.FromMilliseconds(index * 100),
                chunk
            );
        }

        Assert.Equal(1_000, buffer.BufferedBytes);
        Assert.Equal(50, buffer.ChunkCount);

        var snapshot = buffer.Query(1);
        Assert.Equal(1_000, snapshot.Audio.Length);
        Assert.Equal(TimeSpan.FromSeconds(5), snapshot.Duration);
        Assert.Equal(
            TimeSpan.FromMilliseconds(35_995_000),
            snapshot.AvailableStart
        );
    }

    [Fact]
    public void TrackQueriesNeverContainAnotherTracksBytes()
    {
        var buffer = new RollingAudioBuffer(
            Format,
            TimeSpan.FromSeconds(10)
        );

        buffer.Append(1, TimeSpan.Zero, Repeated(1, 100));
        buffer.Append(2, TimeSpan.Zero, Repeated(2, 100));
        buffer.Append(
            1,
            TimeSpan.FromMilliseconds(500),
            Repeated(3, 100)
        );

        Assert.Equal(
            Repeated(1, 100).Concat(Repeated(3, 100)),
            buffer.Query(1).Audio.ToArray()
        );
        Assert.All(
            buffer.Query(2).Audio.ToArray(),
            value => Assert.Equal(2, value)
        );
    }

    [Fact]
    public void QueryReportsUnavailableBeginningAfterMidTrackStart()
    {
        var buffer = new RollingAudioBuffer(
            Format,
            TimeSpan.FromSeconds(5)
        );
        buffer.Append(
            3,
            TimeSpan.FromMinutes(2),
            Repeated(4, 400)
        );

        var snapshot = buffer.Query(
            3,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(122)
        );

        Assert.True(snapshot.IsStartTruncated);
        Assert.False(snapshot.IsEndTruncated);
        Assert.Equal(TimeSpan.FromMinutes(2), snapshot.AvailableStart);
        Assert.Equal(TimeSpan.FromSeconds(122), snapshot.AvailableEnd);
        Assert.Equal(400, snapshot.Audio.Length);
    }

    [Fact]
    public void SubSecondRangeIsFrameAligned()
    {
        var buffer = new RollingAudioBuffer(
            Format,
            TimeSpan.FromSeconds(5)
        );
        buffer.Append(4, TimeSpan.Zero, Repeated(9, 200));

        var snapshot = buffer.Query(
            4,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(250)
        );

        Assert.Equal(30, snapshot.Audio.Length);
        Assert.Equal(TimeSpan.FromMilliseconds(150), snapshot.Duration);
    }

    [Fact]
    public void SnapshotRemainsStableWhileNewDataIsAppended()
    {
        var buffer = new RollingAudioBuffer(
            Format,
            TimeSpan.FromSeconds(1)
        );
        buffer.Append(5, TimeSpan.Zero, Repeated(1, 100));

        var snapshot = buffer.Query(5);
        buffer.Append(
            6,
            TimeSpan.Zero,
            Repeated(2, 200)
        );

        Assert.Equal(Repeated(1, 100), snapshot.Audio.ToArray());
        Assert.All(
            buffer.Query(6).Audio.ToArray(),
            value => Assert.Equal(2, value)
        );
    }

    private static byte[] Repeated(byte value, int count) =>
        Enumerable.Repeat(value, count).ToArray();
}
