using System.ComponentModel;
using System.Windows;

namespace Hookline.App;

public partial class UrlImportWindow : Window
{
    private readonly UrlImportViewModel _viewModel;
    private bool _allowClose;
    private bool _isHosted;
    private bool _disposed;

    internal UrlImportWindow(UrlImportViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => UrlTextBox.Focus();
    }

    internal event EventHandler<UrlImportCompletedEventArgs>?
        ImportCompleted;

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
        Content = null;
        content.DataContext = _viewModel;
        _ = Dispatcher.BeginInvoke(() => UrlTextBox.Focus());
        return content;
    }

    internal void DisposeHosted()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.Dispose();
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        _viewModel.Cancel();
        if (_isHosted)
        {
            DisposeHosted();
        }
        else
        {
            Close();
        }
    }

    protected override void OnClosing(CancelEventArgs args)
    {
        if (_viewModel.IsBusy && !_allowClose)
        {
            args.Cancel = true;
            _viewModel.Cancel();
        }

        base.OnClosing(args);
    }

    protected override void OnClosed(EventArgs args)
    {
        DisposeHosted();
        base.OnClosed(args);
    }

    private async void OnFetchClick(
        object sender,
        RoutedEventArgs args
    ) => await _viewModel.ResolveAsync();

    private async void OnImportClick(
        object sender,
        RoutedEventArgs args
    )
    {
        var imported = await _viewModel.ImportAsync();
        if (imported is null)
        {
            return;
        }

        ImportCompleted?.Invoke(
            this,
            new UrlImportCompletedEventArgs(imported)
        );
        if (_isHosted)
        {
            return;
        }

        _allowClose = true;
        Close();
    }

    private void OnCancelClick(
        object sender,
        RoutedEventArgs args
    ) => _viewModel.Cancel();

    private void OnCloseClick(
        object sender,
        RoutedEventArgs args
    )
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.Cancel();
            return;
        }

        _allowClose = true;
        if (_isHosted)
        {
            HostCloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Close();
        }
    }

    private void OnDismissNoticeClick(
        object sender,
        RoutedEventArgs args
    ) => _viewModel.DismissNotice();
}
