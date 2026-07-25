using System.Collections.Concurrent;

namespace Hookline.NowPlaying.Tests;

public sealed class AsyncDebouncerTests
{
    [Fact]
    public async Task OnlyRunsTheLastTriggeredAction()
    {
        var delays =
            new ConcurrentQueue<TaskCompletionSource>();
        var actions = new ConcurrentQueue<int>();

        Task Delay(TimeSpan _, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            cancellationToken.Register(() =>
                completion.TrySetCanceled(cancellationToken)
            );
            delays.Enqueue(completion);
            return completion.Task;
        }

        await using var debouncer = new AsyncDebouncer(
            TimeSpan.FromSeconds(1),
            Delay
        );

        debouncer.Trigger(
            _ =>
            {
                actions.Enqueue(1);
                return Task.CompletedTask;
            },
            CancellationToken.None
        );
        await WaitForCountAsync(delays, 1);

        debouncer.Trigger(
            _ =>
            {
                actions.Enqueue(2);
                return Task.CompletedTask;
            },
            CancellationToken.None
        );
        await WaitForCountAsync(delays, 2);

        Assert.True(delays.TryDequeue(out var firstDelay));
        Assert.True(firstDelay.Task.IsCanceled);
        Assert.True(delays.TryDequeue(out var finalDelay));
        finalDelay.SetResult();

        await debouncer.PendingTask;

        Assert.Equal([2], actions);
    }

    private static async Task WaitForCountAsync(
        ConcurrentQueue<TaskCompletionSource> queue,
        int count
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (queue.Count < count && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(queue.Count >= count);
    }
}
