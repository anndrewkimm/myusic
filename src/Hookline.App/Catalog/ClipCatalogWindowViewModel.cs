using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Hookline.App.Catalog;

public sealed class ClipCatalogWindowViewModel
    : INotifyPropertyChanged,
      IDisposable
{
    private readonly ClipCatalogService _catalog;
    private readonly IClipPlaybackPlayer _player;
    private readonly IClipRetrimLauncher _retrimLauncher;
    private readonly IClipRevealService _revealService;
    private readonly CancellationTokenSource _lifetimeCancellation =
        new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private CatalogSortOrder _sortOrder;
    private string _statusMessage = string.Empty;
    private bool _isLoading;
    private bool _disposed;

    public ClipCatalogWindowViewModel(
        ClipCatalogService catalog,
        IClipPlaybackPlayer player,
        IClipRetrimLauncher retrimLauncher,
        IClipRevealService? revealService = null
    )
    {
        _catalog =
            catalog
            ?? throw new ArgumentNullException(nameof(catalog));
        _player =
            player
            ?? throw new ArgumentNullException(nameof(player));
        _retrimLauncher =
            retrimLauncher
            ?? throw new ArgumentNullException(
                nameof(retrimLauncher)
            );
        _revealService =
            revealService ?? new ExplorerClipRevealService();
        _player.PlaybackChanged += OnPlaybackChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ClipCatalogItemViewModel> Items
    {
        get;
    } = [];

    public CatalogSortOrder SortOrder
    {
        get => _sortOrder;
        private set
        {
            if (_sortOrder == value)
            {
                return;
            }

            _sortOrder = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsEmpty => Items.Count == 0 && !IsLoading;

    public async Task LoadAsync()
    {
        if (_disposed)
        {
            return;
        }

        var lockAcquired = false;
        try
        {
            await _loadLock.WaitAsync(
                _lifetimeCancellation.Token
            );
            lockAcquired = true;
            IsLoading = true;
            OnPropertyChanged(nameof(IsEmpty));
            var entries = await _catalog.GetAllAsync(
                SortOrder,
                _lifetimeCancellation.Token
            );
            var playingId = _player.IsPlaying
                ? _player.CurrentClipId
                : null;
            Items.Clear();
            foreach (var entry in entries)
            {
                Items.Add(
                    new ClipCatalogItemViewModel(entry)
                    {
                        IsPlaying = entry.Id == playingId,
                    }
                );
            }

            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = Format(
                AppStrings.CatalogLoadFailed,
                exception.Message
            );
        }
        finally
        {
            if (lockAcquired)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
                _loadLock.Release();
            }
        }
    }

    public async Task SetSortOrderAsync(
        CatalogSortOrder sortOrder
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (SortOrder == sortOrder && Items.Count > 0)
        {
            return;
        }

        SortOrder = sortOrder;
        await LoadAsync();
    }

    public async Task TogglePlaybackAsync(
        ClipCatalogItemViewModel item
    )
    {
        ArgumentNullException.ThrowIfNull(item);
        if (
            _player.IsPlaying
            && _player.CurrentClipId == item.Id
        )
        {
            _player.Stop();
            return;
        }

        var refreshed = await RefreshAsync(item);
        if (refreshed is null || refreshed.IsMissing)
        {
            return;
        }

        try
        {
            _player.Play(item.Id, refreshed.FilePath);
            StatusMessage = string.Empty;
        }
        catch (Exception exception)
        {
            StatusMessage = Format(
                AppStrings.CatalogPlayFailed,
                exception.Message
            );
        }
    }

    public void BeginRename(ClipCatalogItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.BeginEdit();
    }

    public void CancelRename(ClipCatalogItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.CancelEdit();
    }

    public async Task RenameAsync(ClipCatalogItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.EditTitle))
        {
            StatusMessage = AppStrings.CatalogRenameRequired;
            return;
        }

        var refreshed = await RefreshAsync(item);
        if (refreshed is null || refreshed.IsMissing)
        {
            return;
        }

        if (_player.CurrentClipId == item.Id)
        {
            _player.Stop();
        }

        try
        {
            await _catalog.RenameAsync(
                item.Id,
                item.EditTitle,
                _lifetimeCancellation.Token
            );
            item.FinishEdit();
            StatusMessage = AppStrings.CatalogTitleUpdated;
            await LoadAsync();
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = Format(
                AppStrings.CatalogRenameFailed,
                exception.Message
            );
        }
    }

    public async Task DeleteAsync(
        ClipCatalogItemViewModel item
    )
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_player.CurrentClipId == item.Id)
        {
            _player.Stop();
        }

        try
        {
            await _catalog.DeleteAsync(
                item.Id,
                _lifetimeCancellation.Token
            );
            StatusMessage = AppStrings.CatalogDeleted;
            await LoadAsync();
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = Format(
                AppStrings.CatalogDeleteFailed,
                exception.Message
            );
        }
    }

    public async Task RetrimAsync(
        ClipCatalogItemViewModel item
    )
    {
        ArgumentNullException.ThrowIfNull(item);
        var refreshed = await RefreshAsync(item);
        if (refreshed is null || refreshed.IsMissing)
        {
            return;
        }

        try
        {
            var result = await _retrimLauncher.OpenAsync(
                refreshed,
                _lifetimeCancellation.Token
            );
            StatusMessage =
                result == ClipRetrimResult.BufferUnavailable
                    ? AppStrings.CatalogRetrimUnavailable
                    : string.Empty;
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = Format(
                AppStrings.CatalogRetrimFailed,
                exception.Message
            );
        }
    }

    public async Task RevealAsync(
        ClipCatalogItemViewModel item
    )
    {
        ArgumentNullException.ThrowIfNull(item);
        var refreshed = await RefreshAsync(item);
        if (refreshed is null || refreshed.IsMissing)
        {
            return;
        }

        try
        {
            _revealService.Reveal(refreshed.FilePath);
            StatusMessage = string.Empty;
        }
        catch (Exception exception)
        {
            StatusMessage = Format(
                AppStrings.CatalogRevealFailed,
                exception.Message
            );
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _player.PlaybackChanged -= OnPlaybackChanged;
        _player.Dispose();

        // A pending LoadAsync can still be unwinding through its finally
        // block. Leave these lightweight synchronization objects for the GC
        // so shutdown cannot race with Release or token registration cleanup.
    }

    private async Task<ClipCatalogEntry?> RefreshAsync(
        ClipCatalogItemViewModel item
    )
    {
        try
        {
            var refreshed =
                await _catalog.RefreshAvailabilityAsync(
                    item.Id,
                    _lifetimeCancellation.Token
                );
            if (refreshed is null)
            {
                Items.Remove(item);
                OnPropertyChanged(nameof(IsEmpty));
                StatusMessage = AppStrings.CatalogEntryNotFound;
                return null;
            }

            item.Update(refreshed);
            if (refreshed.IsMissing)
            {
                StatusMessage = Format(
                    AppStrings.CatalogFileMissing,
                    refreshed.FilePath
                );
            }

            return refreshed;
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            return null;
        }
    }

    private void OnPlaybackChanged(
        object? sender,
        ClipPlaybackChangedEventArgs args
    )
    {
        foreach (var item in Items)
        {
            item.IsPlaying =
                args.IsPlaying && item.Id == args.ClipId;
        }

        if (args.Error is not null)
        {
            StatusMessage = Format(
                AppStrings.CatalogPlayFailed,
                args.Error.Message
            );
        }
    }

    private static string Format(string format, string value) =>
        string.Format(
            CultureInfo.CurrentCulture,
            format,
            value
        );

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null
    ) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName)
        );
}
