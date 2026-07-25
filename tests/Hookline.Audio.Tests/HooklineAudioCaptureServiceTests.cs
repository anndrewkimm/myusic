using System.Collections.Concurrent;
using Hookline.Audio.Capture;
using Hookline.Audio.Tests.Fakes;
using Hookline.NowPlaying;

namespace Hookline.Audio.Tests;

public sealed class HooklineAudioCaptureServiceTests
{
    private static readonly PcmAudioFormat Format =
        new(sampleRate: 100, bitsPerSample: 16, channels: 1);

    [Fact]
    public async Task ExcludesPausedAndAdvertisementPacketsAndSegmentsReplays()
    {
        var watcher = new FakeNowPlayingWatcher();
        watcher.SetTrack(1);
        var factory = new FakeAudioCaptureBackendFactory(Format);
        await using var service = new HooklineAudioCaptureService(
            watcher,
            factory,
            new AudioCaptureOptions
            {
                BufferWindow = TimeSpan.FromSeconds(10),
            },
            Format
        );
        await service.StartAsync();

        factory.Current.Emit(1, 20);
        factory.Current.Emit(2, 20);

        watcher.SetPlaybackState(PlaybackState.Paused);
        factory.Current.Emit(3, 20);
        watcher.SetPlaybackState(PlaybackState.Playing);

        watcher.SetTrack(2, isLikelyAd: true);
        factory.Current.Emit(4, 20);
        factory.Current.Emit(5, 20);

        watcher.SetTrack(3);
        factory.Current.Emit(6, 20);
        factory.Current.Emit(7, 20);

        Assert.All(
            service.Query(1).Audio.ToArray(),
            value => Assert.Equal(2, value)
        );
        Assert.Empty(service.Query(2).Audio.ToArray());
        Assert.All(
            service.Query(3).Audio.ToArray(),
            value => Assert.Equal(7, value)
        );
    }

    [Fact]
    public async Task ReportsExplicitFallbackMode()
    {
        var watcher = new FakeNowPlayingWatcher();
        watcher.SetTrack(1);
        var factory = new FakeAudioCaptureBackendFactory(
            Format,
            AudioCaptureMode.SystemLoopback,
            "process capture unavailable"
        );
        await using var service = new HooklineAudioCaptureService(
            watcher,
            factory,
            format: Format
        );
        AudioCaptureStatusChangedEventArgs? latest = null;
        service.StatusChanged += (_, args) => latest = args;

        await service.StartAsync();

        Assert.Equal(
            AudioCaptureStatus.FallbackLoopback,
            service.Status
        );
        Assert.Equal(
            AudioCaptureMode.SystemLoopback,
            service.CaptureMode
        );
        Assert.Equal("process capture unavailable", latest?.Detail);
    }

    [Fact]
    public async Task DetectsCaptureStallWhileTrackIsPlaying()
    {
        var watcher = new FakeNowPlayingWatcher();
        watcher.SetTrack(1);
        var factory = new FakeAudioCaptureBackendFactory(Format);
        await using var service = new HooklineAudioCaptureService(
            watcher,
            factory,
            new AudioCaptureOptions
            {
                BufferWindow = TimeSpan.FromSeconds(5),
                StallTimeout = TimeSpan.FromMilliseconds(40),
                StallCheckInterval = TimeSpan.FromMilliseconds(10),
            },
            Format
        );

        await service.StartAsync();
        await WaitUntilAsync(
            () => service.Status == AudioCaptureStatus.Stalled
        );

        Assert.Equal(AudioCaptureStatus.Stalled, service.Status);
    }

    [Fact]
    public async Task BackendFailureIsVisibleAndTriggersRestart()
    {
        var watcher = new FakeNowPlayingWatcher();
        watcher.SetTrack(1);
        var factory = new FakeAudioCaptureBackendFactory(Format);
        await using var service = new HooklineAudioCaptureService(
            watcher,
            factory,
            format: Format
        );
        var statuses = new ConcurrentQueue<AudioCaptureStatus>();
        service.StatusChanged += (_, args) =>
            statuses.Enqueue(args.Status);
        await service.StartAsync();

        factory.Current.Fail(new InvalidOperationException("device changed"));
        await WaitUntilAsync(() => factory.Backends.Count == 2);

        Assert.Contains(AudioCaptureStatus.Stalled, statuses);
        Assert.Equal(AudioCaptureStatus.Running, service.Status);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(2)
        );
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
