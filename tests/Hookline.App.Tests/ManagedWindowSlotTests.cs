namespace Hookline.App.Tests;

public sealed class ManagedWindowSlotTests
{
    [Fact]
    public void WorkspaceShowFailureAllowsTheNextHotkeyToRetry()
    {
        var slot = new ManagedWindowSlot<FakeWindow>();
        var failed = new FakeWindow { ThrowOnShow = true };
        var registryEntryRemoved = false;

        Assert.Throws<InvalidOperationException>(
            () =>
                slot.TryShowNew(
                    () => failed,
                    SubscribeClosed,
                    Show,
                    Close,
                    _ => registryEntryRemoved = true
                )
        );

        Assert.Null(slot.Current);
        Assert.Equal(1, failed.CloseCallCount);
        Assert.True(registryEntryRemoved);

        var retry = new FakeWindow();
        Assert.True(ShowNew(slot, retry));
        Assert.Same(retry, slot.Current);
        Assert.Equal(1, retry.ShowCallCount);
    }

    [Fact]
    public void HealthyExistingWindowIsActivatedAndRetained()
    {
        var slot = new ManagedWindowSlot<FakeWindow>();
        var window = new FakeWindow();
        ShowNew(slot, window);

        var activated = slot.TryActivateExisting(
            candidate => candidate.IsUsable,
            Activate,
            Close
        );

        Assert.True(activated);
        Assert.Same(window, slot.Current);
        Assert.Equal(1, window.ActivateCallCount);
        Assert.Equal(0, window.CloseCallCount);
    }

    [Fact]
    public void ConstructionFailureAlsoLeavesTheSlotRetryable()
    {
        var slot = new ManagedWindowSlot<FakeWindow>();

        Assert.Throws<InvalidOperationException>(
            () =>
                slot.TryShowNew(
                    () =>
                        throw new InvalidOperationException(
                            "Injected construction failure."
                        ),
                    SubscribeClosed,
                    Show,
                    Close
                )
        );

        Assert.Null(slot.Current);
        var retry = new FakeWindow();
        Assert.True(ShowNew(slot, retry));
        Assert.Same(retry, slot.Current);
    }

    [Fact]
    public void UnusableExistingWindowIsDiscardedAndRecreated()
    {
        var slot = new ManagedWindowSlot<FakeWindow>();
        var stale = new FakeWindow();
        ShowNew(slot, stale);
        stale.IsUsable = false;

        var activated = slot.TryActivateExisting(
            window => window.IsUsable,
            Activate,
            Close
        );

        Assert.False(activated);
        Assert.Null(slot.Current);
        Assert.Equal(1, stale.CloseCallCount);

        var replacement = new FakeWindow();
        Assert.True(ShowNew(slot, replacement));
        Assert.Same(replacement, slot.Current);
    }

    [Fact]
    public void ActivationFailureAlsoDiscardsTheBrokenReference()
    {
        var slot = new ManagedWindowSlot<FakeWindow>();
        var broken = new FakeWindow
        {
            ThrowOnActivate = true,
        };
        ShowNew(slot, broken);

        var activated = slot.TryActivateExisting(
            window => window.IsUsable,
            Activate,
            Close
        );

        Assert.False(activated);
        Assert.Null(slot.Current);
        Assert.Equal(1, broken.CloseCallCount);
    }

    [Fact]
    public void ReentrantOpenRequestDoesNotCreateOrSubscribeTwice()
    {
        var slot = new ManagedWindowSlot<FakeWindow>();
        var window = new FakeWindow();
        var nestedRequestHandled = false;

        var opened = slot.TryShowNew(
            () => window,
            SubscribeClosed,
            candidate =>
            {
                candidate.ShowCallCount++;
                nestedRequestHandled =
                    slot.TryActivateExisting(
                        value => value.IsUsable,
                        Activate,
                        Close
                    );
                candidate.IsUsable = true;
            },
            Close
        );

        Assert.True(opened);
        Assert.True(nestedRequestHandled);
        Assert.Same(window, slot.Current);
        Assert.Equal(1, window.ShowCallCount);
        Assert.Equal(1, window.ClosedSubscriptionCount);
        Assert.Equal(0, window.ActivateCallCount);
    }

    [Fact]
    public void ClosingAHealthyWindowClearsItsReference()
    {
        var slot = new ManagedWindowSlot<FakeWindow>();
        var window = new FakeWindow();
        ShowNew(slot, window);

        window.RaiseClosed();

        Assert.Null(slot.Current);
    }

    [Fact]
    public void HiddenWorkspaceIsRestoredByTheNextOpenRequest()
    {
        var slot = new ManagedWindowSlot<FakeWindow>();
        var workspace = new FakeWindow();
        ShowNew(slot, workspace);
        workspace.IsVisible = false;

        var activated = slot.TryActivateExisting(
            window => window.IsUsable,
            window =>
            {
                if (!window.IsVisible)
                {
                    window.ShowCallCount++;
                    window.IsVisible = true;
                }

                Activate(window);
            },
            Close
        );

        Assert.True(activated);
        Assert.Same(workspace, slot.Current);
        Assert.True(workspace.IsVisible);
        Assert.Equal(2, workspace.ShowCallCount);
        Assert.Equal(1, workspace.ActivateCallCount);
    }

    private static bool ShowNew(
        ManagedWindowSlot<FakeWindow> slot,
        FakeWindow window
    ) =>
        slot.TryShowNew(
            () => window,
            SubscribeClosed,
            Show,
            Close
        );

    private static void SubscribeClosed(
        FakeWindow window,
        EventHandler handler
    )
    {
        window.ClosedSubscriptionCount++;
        window.Closed += handler;
    }

    private static void Show(FakeWindow window)
    {
        window.ShowCallCount++;
        if (window.ThrowOnShow)
        {
            throw new InvalidOperationException(
                "Injected show failure."
            );
        }

        window.IsUsable = true;
        window.IsVisible = true;
    }

    private static void Activate(FakeWindow window)
    {
        window.ActivateCallCount++;
        if (window.ThrowOnActivate)
        {
            throw new InvalidOperationException(
                "Injected activation failure."
            );
        }
    }

    private static void Close(FakeWindow window)
    {
        window.CloseCallCount++;
        window.IsUsable = false;
        window.IsVisible = false;
        window.RaiseClosed();
    }

    private sealed class FakeWindow
    {
        public event EventHandler? Closed;

        public bool IsUsable { get; set; }

        public bool IsVisible { get; set; }

        public bool ThrowOnShow { get; init; }

        public bool ThrowOnActivate { get; init; }

        public int ShowCallCount { get; set; }

        public int ActivateCallCount { get; set; }

        public int CloseCallCount { get; set; }

        public int ClosedSubscriptionCount { get; set; }

        public void RaiseClosed() =>
            Closed?.Invoke(this, EventArgs.Empty);
    }
}
