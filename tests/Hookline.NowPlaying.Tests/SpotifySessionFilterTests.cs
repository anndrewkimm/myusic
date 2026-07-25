using Hookline.NowPlaying.SystemMedia;
using Hookline.NowPlaying.Tests.Fakes;

namespace Hookline.NowPlaying.Tests;

public sealed class SpotifySessionFilterTests
{
    [Theory]
    [InlineData("Spotify.exe")]
    [InlineData("spotify.EXE")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify")]
    public void RecognizesKnownSpotifySourceIds(string sourceId)
    {
        Assert.True(SpotifySessionFilter.IsSpotifySource(sourceId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("chrome.exe")]
    [InlineData("com.example.spotify-player")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!NotSpotify")]
    public void RejectsNonSpotifySourceIds(string? sourceId)
    {
        Assert.False(SpotifySessionFilter.IsSpotifySource(sourceId));
    }

    [Fact]
    public void SelectPrefersPlayingSpotifySession()
    {
        var paused = new FakeSystemMediaSession("Spotify.exe")
        {
            PlaybackStatus = SystemPlaybackStatus.Paused,
        };
        var playing = new FakeSystemMediaSession(
            "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"
        )
        {
            PlaybackStatus = SystemPlaybackStatus.Playing,
        };
        var browser = new FakeSystemMediaSession("chrome.exe")
        {
            PlaybackStatus = SystemPlaybackStatus.Playing,
        };

        var result = SpotifySessionFilter.Select([browser, paused, playing]);

        Assert.Same(playing, result);
    }
}
