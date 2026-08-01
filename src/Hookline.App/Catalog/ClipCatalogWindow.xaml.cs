using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Hookline.App.Catalog;

public partial class ClipCatalogWindow : Window
{
    private readonly ClipCatalogWindowViewModel _viewModel;
    private readonly ClipCatalogService _catalog;
    private bool _loaded;
    private bool _isHosted;
    private bool _disposed;

    public ClipCatalogWindow(
        ClipCatalogWindowViewModel viewModel,
        ClipCatalogService catalog
    )
    {
        _viewModel =
            viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        _catalog =
            catalog
            ?? throw new ArgumentNullException(nameof(catalog));
        InitializeComponent();
        DataContext = viewModel;
        _catalog.Changed += OnCatalogChanged;
    }

    internal event EventHandler? HostCloseRequested;

    internal FrameworkElement TakeContentForHost()
    {
        if (Content is not FrameworkElement content)
        {
            throw new InvalidOperationException(
                AppStrings.WorkspaceViewUnavailable
            );
        }

        _isHosted = true;
        HeaderBar.Visibility = Visibility.Collapsed;
        Content = null;
        content.DataContext = _viewModel;
        return content;
    }

    internal async Task LoadHostedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.LoadAsync();
    }

    internal void DisposeHosted()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _catalog.Changed -= OnCatalogChanged;
        _viewModel.Dispose();
    }

    protected override void OnClosed(EventArgs args)
    {
        DisposeHosted();
        base.OnClosed(args);
    }

    private async void OnLoaded(
        object sender,
        RoutedEventArgs args
    )
    {
        _loaded = true;
        await _viewModel.LoadAsync();
    }

    private async void OnSortChanged(
        object sender,
        SelectionChangedEventArgs args
    )
    {
        if (!_loaded)
        {
            return;
        }

        var sortOrder = SortPicker.SelectedIndex == 1
            ? CatalogSortOrder.Artist
            : CatalogSortOrder.MostRecent;
        await _viewModel.SetSortOrderAsync(sortOrder);
    }

    private async void OnPlayClick(
        object sender,
        RoutedEventArgs args
    )
    {
        if (GetItem(sender) is { } item)
        {
            await _viewModel.TogglePlaybackAsync(item);
        }
    }

    private async void OnRetrimClick(
        object sender,
        RoutedEventArgs args
    )
    {
        if (GetItem(sender) is { } item)
        {
            await _viewModel.RetrimAsync(item);
        }
    }

    private async void OnRevealClick(
        object sender,
        RoutedEventArgs args
    )
    {
        if (GetItem(sender) is { } item)
        {
            await _viewModel.RevealAsync(item);
        }
    }

    private void OnRenameClick(
        object sender,
        RoutedEventArgs args
    )
    {
        if (GetItem(sender) is { } item)
        {
            _viewModel.BeginRename(item);
        }
    }

    private async void OnSaveRenameClick(
        object sender,
        RoutedEventArgs args
    )
    {
        if (GetItem(sender) is { } item)
        {
            await _viewModel.RenameAsync(item);
        }
    }

    private void OnCancelRenameClick(
        object sender,
        RoutedEventArgs args
    )
    {
        if (GetItem(sender) is { } item)
        {
            _viewModel.CancelRename(item);
        }
    }

    private async void OnDeleteClick(
        object sender,
        RoutedEventArgs args
    )
    {
        if (GetItem(sender) is not { } item)
        {
            return;
        }

        var message = string.Format(
            CultureInfo.CurrentCulture,
            AppStrings.CatalogDeleteConfirm,
            item.DisplayTitle
        );
        var result = MessageBox.Show(
            Window.GetWindow((DependencyObject)sender) ?? this,
            message,
            AppStrings.CatalogDeleteConfirmTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No
        );
        if (result == MessageBoxResult.Yes)
        {
            await _viewModel.DeleteAsync(item);
        }
    }

    private async void OnRenameTextBoxKeyDown(
        object sender,
        KeyEventArgs args
    )
    {
        if (GetItem(sender) is not { } item)
        {
            return;
        }

        if (args.Key == Key.Enter)
        {
            args.Handled = true;
            await _viewModel.RenameAsync(item);
        }
        else if (args.Key == Key.Escape)
        {
            args.Handled = true;
            _viewModel.CancelRename(item);
        }
    }

    private void OnRenameTextBoxIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs args
    )
    {
        if (
            sender is not TextBox textBox
            || !textBox.IsVisible
        )
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (textBox.IsVisible)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        });
    }

    private void OnWindowPreviewKeyDown(
        object sender,
        KeyEventArgs args
    )
    {
        if (
            args.Key == Key.Escape
            && Keyboard.FocusedElement is not TextBox
        )
        {
            args.Handled = true;
            if (_isHosted)
            {
                HostCloseRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Close();
            }
        }
    }

    private void OnCatalogChanged(object? sender, EventArgs args)
    {
        if (!Dispatcher.HasShutdownStarted)
        {
            _ = Dispatcher.BeginInvoke(
                async () => await _viewModel.LoadAsync()
            );
        }
    }

    private void OnHeaderMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs args
    )
    {
        if (args.ChangedButton == MouseButton.Left)
        {
            (Window.GetWindow((DependencyObject)sender) ?? this)
                .DragMove();
        }
    }

    private void OnCloseClick(
        object sender,
        RoutedEventArgs args
    )
    {
        if (_isHosted)
        {
            HostCloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Close();
        }
    }

    private static ClipCatalogItemViewModel? GetItem(
        object sender
    ) =>
        (sender as FrameworkElement)?.DataContext
            as ClipCatalogItemViewModel;
}
