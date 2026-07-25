using Hookline.NowPlaying.SystemMedia;

namespace Hookline.NowPlaying.Tests;

public sealed class TrackChangeDetectorTests
{
    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public void DetectsResetToStartAsReplay()
    {
        var previous = Timeline(
            position: TimeSpan.FromSeconds(100),
            updatedAt: Start
        );
        var current = Timeline(
            position: TimeSpan.FromSeconds(1),
            updatedAt: Start.AddSeconds(2)
        );

        Assert.True(
            TrackChangeDetector.IsReplay(
                previous,
                current,
                TimeSpan.FromMinutes(4),
                PlaybackState.Playing
            )
        );
    }

    [Fact]
    public void DoesNotTreatOrdinaryBackwardSeekAsReplay()
    {
        var previous = Timeline(
            position: TimeSpan.FromSeconds(100),
            updatedAt: Start
        );
        var current = Timeline(
            position: TimeSpan.FromSeconds(30),
            updatedAt: Start.AddSeconds(1)
        );

        Assert.False(
            TrackChangeDetector.IsReplay(
                previous,
                current,
                TimeSpan.FromMinutes(4),
                PlaybackState.Playing
            )
        );
    }

    [Fact]
    public void DoesNotTreatSmallTimelineCorrectionAsReplay()
    {
        var previous = Timeline(
            position: TimeSpan.FromSeconds(2),
            updatedAt: Start
        );
        var current = Timeline(
            position: TimeSpan.FromSeconds(1),
            updatedAt: Start.AddSeconds(1)
        );

        Assert.False(
            TrackChangeDetector.IsReplay(
                previous,
                current,
                TimeSpan.FromMinutes(4),
                PlaybackState.Playing
            )
        );
    }

    [Theory]
    [InlineData("Advertisement", true)]
    [InlineData(" advertisement ", true)]
    [InlineData("Ad", false)]
    [InlineData("Spotify", false)]
    [InlineData("Advertisement Blues", false)]
    public void AdDetectionOnlyUsesExplicitAdvertisementTitle(
        string title,
        bool expected
    )
    {
        Assert.Equal(expected, TrackChangeDetector.IsLikelyAd(title));
    }

    private static SystemTimelineProperties Timeline(
        TimeSpan position,
        DateTimeOffset updatedAt
    ) =>
        new(
            TimeSpan.Zero,
            TimeSpan.FromMinutes(4),
            position,
            updatedAt
        );
}
