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

    public MixWindow(MixWindowViewModel viewModel)
    {
        _viewModel =
            viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs args)
    {
        _viewModel.Dispose();
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
    ) => await ImportSourceAsync(MixSourceSlot.First);

    private async void OnImportSecondClick(
        object sender,
        RoutedEventArgs args
    ) => await ImportSourceAsync(MixSourceSlot.Second);

    private async Task ImportSourceAsync(MixSourceSlot slot)
    {
        var dialog = new FileOpenDialog
        {
            Title = AppStrings.ChooseAudioFile,
            Filter = AppStrings.AudioFileFilter,
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
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

    private void OnWindowPreviewKeyDown(
        object sender,
        KeyEventArgs args
    )
    {
        if (args.Key == Key.Escape)
        {
            args.Handled = true;
            Close();
        }
    }

    private void OnHeaderMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs args
    )
    {
        if (args.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnCloseClick(
        object sender,
        RoutedEventArgs args
    ) => Close();
}
