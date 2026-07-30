namespace Hookline.App;

internal sealed class ManagedWindowSlot<TWindow>
    where TWindow : class
{
    private TWindow? _current;
    private bool _isOpening;

    public TWindow? Current => _current;

    public bool TryActivateExisting(
        Func<TWindow, bool> isUsable,
        Action<TWindow> activate,
        Action<TWindow> discard
    )
    {
        ArgumentNullException.ThrowIfNull(isUsable);
        ArgumentNullException.ThrowIfNull(activate);
        ArgumentNullException.ThrowIfNull(discard);

        if (_isOpening)
        {
            return true;
        }

        var window = _current;
        if (window is null)
        {
            return false;
        }

        try
        {
            if (!isUsable(window))
            {
                Clear(window);
                TryDiscard(window, discard);
                return false;
            }

            activate(window);
            return true;
        }
        catch
        {
            Clear(window);
            TryDiscard(window, discard);
            return false;
        }
    }

    public bool TryShowNew(
        Func<TWindow> create,
        Action<TWindow, EventHandler> subscribeClosed,
        Action<TWindow> show,
        Action<TWindow> discard,
        Action<TWindow>? onClosed = null
    )
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(subscribeClosed);
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(discard);

        if (_isOpening || _current is not null)
        {
            return false;
        }

        _isOpening = true;
        TWindow? window = null;
        try
        {
            var createdWindow = create();
            ArgumentNullException.ThrowIfNull(createdWindow);
            window = createdWindow;
            _current = createdWindow;
            subscribeClosed(
                createdWindow,
                (_, _) =>
                {
                    Clear(createdWindow);
                    onClosed?.Invoke(createdWindow);
                }
            );
            show(createdWindow);
            return true;
        }
        catch
        {
            if (window is not null)
            {
                Clear(window);
                TryDiscard(window, discard);
            }

            throw;
        }
        finally
        {
            _isOpening = false;
        }
    }

    public void CloseCurrent(Action<TWindow> close)
    {
        ArgumentNullException.ThrowIfNull(close);
        var window = _current;
        if (window is null)
        {
            return;
        }

        Clear(window);
        TryDiscard(window, close);
    }

    private void Clear(TWindow window)
    {
        if (ReferenceEquals(_current, window))
        {
            _current = null;
        }
    }

    private static void TryDiscard(
        TWindow window,
        Action<TWindow> discard
    )
    {
        try
        {
            discard(window);
        }
        catch
        {
            // The slot is already clear; preserve the original failure.
        }
    }
}
