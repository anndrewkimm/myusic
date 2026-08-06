using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileOpenDialog = Microsoft.Win32.OpenFileDialog;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace Hookline.App.Mixing;

public partial class MixWindow : Window
{
    private readonly MixWindowViewModel _viewModel;
    private bool _loaded;
    private bool _isHosted;
    private bool _disposed;

    public MixWindow(MixWindowViewModel viewModel)
    {
        _viewModel =
            viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
    }

    internal event EventHandler? HostCloseRequested;

    internal event EventHandler<MixSourceEditRequestedEventArgs>?
        EditSourceRequested;

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
        await _viewModel.LoadCatalogAsync();
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
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.LoadCatalogAsync();
    }

    private async void OnFirstCatalogSelectionChanged(
        object sender,
        SelectionChangedEventArgs args
    )
    {
        if (
            _loaded
            && (sender as WpfComboBox)?.SelectedItem
                is MixCatalogSource source
        )
        {
            await _viewModel.LoadCatalogSourceAsync(
                MixSourceSlot.First,
                source
            );
        }
    }

    private async void OnSecondCatalogSelectionChanged(
        object sender,
        SelectionChangedEventArgs args
    )
    {
        if (
            _loaded
            && (sender as WpfComboBox)?.SelectedItem
                is MixCatalogSource source
        )
        {
            await _viewModel.LoadCatalogSourceAsync(
                MixSourceSlot.Second,
                source
            );
        }
    }

    private async void OnImportFirstClick(
        object sender,
        RoutedEventArgs args
    ) =>
        await ImportSourceAsync(
            MixSourceSlot.First,
            Window.GetWindow((DependencyObject)sender)
        );

    private async void OnImportSecondClick(
        object sender,
        RoutedEventArgs args
    ) =>
        await ImportSourceAsync(
            MixSourceSlot.Second,
            Window.GetWindow((DependencyObject)sender)
        );

    private async Task ImportSourceAsync(
        MixSourceSlot slot,
        Window? owner
    )
    {
        var dialog = new FileOpenDialog
        {
            Title = AppStrings.ChooseAudioFile,
            Filter = AppStrings.AudioFileFilter,
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(owner ?? this) == true)
        {
            await _viewModel.ImportSourceAsync(
                slot,
                dialog.FileName
            );
        }
    }

    private async void OnExportClick(
        object sender,
        RoutedEventArgs args
    ) => await _viewModel.ExportAsync();

    private async void OnPreviewMixClick(
        object sender,
        RoutedEventArgs args
    ) => await _viewModel.PreviewAsync();

    private async void OnMashupRecipeClick(
        object sender,
        RoutedEventArgs args
    ) =>
        await _viewModel.SelectRecipeAsync(
            MixRecipe.VocalsAndInstrumentalMashup
        );

    private async void OnSequentialRecipeClick(
        object sender,
        RoutedEventArgs args
    ) =>
        await _viewModel.SelectRecipeAsync(
            MixRecipe.Sequential
        );

    private async void OnCustomRecipeClick(
        object sender,
        RoutedEventArgs args
    ) =>
        await _viewModel.SelectRecipeAsync(MixRecipe.Custom);

    private void OnSwapSourcesClick(
        object sender,
        RoutedEventArgs args
    ) => _viewModel.SwapSources();

    private void OnEditFirstSourceClick(
        object sender,
        RoutedEventArgs args
    ) =>
        EditSourceRequested?.Invoke(
            this,
            new MixSourceEditRequestedEventArgs(
                MixSourceSlot.First
            )
        );

    private void OnEditSecondSourceClick(
        object sender,
        RoutedEventArgs args
    ) =>
        EditSourceRequested?.Invoke(
            this,
            new MixSourceEditRequestedEventArgs(
                MixSourceSlot.Second
            )
        );

    private void OnWindowPreviewKeyDown(
        object sender,
        KeyEventArgs args
    )
    {
        if (args.Key == Key.Escape)
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
}
