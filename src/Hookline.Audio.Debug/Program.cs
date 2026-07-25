using Hookline.Audio;
using Hookline.Audio.Debug;
using Hookline.NowPlaying;

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    shutdown.Cancel();
};

await using var watcher = new SpotifyNowPlayingWatcher();
await using var capture = new HooklineAudioCaptureService(watcher);

capture.StatusChanged += (_, args) =>
    Console.WriteLine(
        DebugStrings.Status(args.Status, args.Mode, args.Detail)
    );
watcher.TrackChanged += (_, args) =>
    Console.WriteLine(
        DebugStrings.Track(
            args.Track.Title,
            args.Track.Artist
        )
    );

Console.WriteLine(DebugStrings.Header);
Console.WriteLine(DebugStrings.Commands);

try
{
    await capture.StartAsync(shutdown.Token);
    while (!shutdown.IsCancellationRequested)
    {
        Console.Write(DebugStrings.Prompt);
        var input = await Console.In.ReadLineAsync(shutdown.Token);
        if (input is null)
        {
            Console.WriteLine(DebugStrings.NoInput);
            break;
        }

        var command = input.Trim();
        if (command.Equals("q", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        if (command.Equals("s", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                DebugStrings.Status(
                    capture.Status,
                    capture.CaptureMode,
                    detail: null
                )
            );
            continue;
        }

        if (
            command.Equals("d", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith(
                "d ",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            var requestedPath =
                command.Length > 1
                    ? command[1..].Trim()
                    : "hookline-debug.wav";
            var outputPath = Path.GetFullPath(requestedPath);
            try
            {
                var snapshot = capture.QueryRecentCurrentTrack(
                    TimeSpan.FromSeconds(30)
                );
                if (snapshot.Audio.IsEmpty)
                {
                    throw new InvalidOperationException(
                        DebugStrings.NoBufferedAudio
                    );
                }

                await WavFileExporter.WriteAsync(
                    outputPath,
                    snapshot,
                    shutdown.Token
                );
                Console.WriteLine(
                    DebugStrings.Dumped(
                        outputPath,
                        snapshot.Duration,
                        snapshot.IsStartTruncated
                    )
                );
            }
            catch (Exception exception)
                when (
                    exception is not OperationCanceledException
                    || !shutdown.IsCancellationRequested
                )
            {
                Console.Error.WriteLine(
                    DebugStrings.DumpFailed(exception.Message)
                );
            }

            continue;
        }

        if (command.Length > 0)
        {
            Console.WriteLine(
                DebugStrings.UnknownCommand(command)
            );
        }
    }
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
}
catch (Exception exception)
{
    Console.Error.WriteLine(DebugStrings.Fatal(exception.Message));
    Environment.ExitCode = 1;
}
