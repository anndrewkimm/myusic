namespace Hookline.NowPlaying;

internal sealed class AsyncDebouncer(
    TimeSpan delay,
    Func<TimeSpan, CancellationToken, Task>? delayAsync = null
) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync =
        delayAsync
        ?? (static (duration, token) => Task.Delay(duration, token));
    private CancellationTokenSource? _pendingCancellation;
    private Task _pendingTask = Task.CompletedTask;

    internal Task PendingTask
    {
        get
        {
            lock (_gate)
            {
                return _pendingTask;
            }
        }
    }

    public void Trigger(
        Func<CancellationToken, Task> action,
        CancellationToken lifetimeToken
    )
    {
        ArgumentNullException.ThrowIfNull(action);

        var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        CancellationTokenSource? previousCancellation;

        lock (_gate)
        {
            previousCancellation = _pendingCancellation;
            _pendingCancellation = cancellation;
            _pendingTask = RunAsync(cancellation, action);
        }

        CancelSafely(previousCancellation);
    }

    public async Task CancelAsync()
    {
        Task pendingTask;
        CancellationTokenSource? pendingCancellation;

        lock (_gate)
        {
            pendingCancellation = _pendingCancellation;
            pendingTask = _pendingTask;
        }

        CancelSafely(pendingCancellation);
        await pendingTask.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await CancelAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(
        CancellationTokenSource cancellation,
        Func<CancellationToken, Task> action
    )
    {
        try
        {
            await Task.Yield();
            await _delayAsync(delay, cancellation.Token).ConfigureAwait(false);
            await action(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pendingCancellation, cancellation))
                {
                    _pendingCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private static void CancelSafely(
        CancellationTokenSource? cancellationTokenSource
    )
    {
        try
        {
            cancellationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
