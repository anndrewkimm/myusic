using System.ComponentModel;
using System.Windows;

namespace Hookline.App;

public partial class UrlImportWindow : Window
{
    private readonly UrlImportViewModel _viewModel;
    private bool _allowClose;

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

    public void CloseForShutdown()
    {
        _allowClose = true;
        _viewModel.Cancel();
        Close();
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
        _viewModel.Dispose();
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
        Close();
    }

    private void OnDismissNoticeClick(
        object sender,
        RoutedEventArgs args
    ) => _viewModel.DismissNotice();
}
