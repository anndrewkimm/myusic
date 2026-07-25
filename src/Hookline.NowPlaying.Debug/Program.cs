using Hookline.NowPlaying;
using Hookline.NowPlaying.Debug;

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    shutdown.Cancel();
};

await using var watcher = new SpotifyNowPlayingWatcher();

watcher.StatusChanged += (_, args) =>
{
    var message = DebugStrings.StatusMessage(args.Status);
    if (!string.IsNullOrWhiteSpace(args.Detail))
    {
        message = $"{message}: {args.Detail}";
    }

    Console.WriteLine(DebugStrings.LogLine(message));
};

watcher.PlaybackStateChanged += (_, args) =>
    Console.WriteLine(
        DebugStrings.LogLine(
            DebugStrings.PlaybackMessage(args.CurrentState)
        )
    );

watcher.TrackChanged += (_, args) =>
{
    var track = args.Track;
    Console.WriteLine(
        DebugStrings.LogLine(DebugStrings.TrackMessage(track))
    );
};

Console.WriteLine(DebugStrings.Header);
Console.WriteLine(DebugStrings.ExitHint);

try
{
    await watcher.StartAsync(shutdown.Token);
    await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
}
catch (Exception exception)
{
    Console.Error.WriteLine(DebugStrings.FatalError(exception.Message));
    Environment.ExitCode = 1;
}
