using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Hookline.Audio;
using Forms = System.Windows.Forms;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Hookline.App;

public partial class TrimWindow : Window
{
    private static readonly TimeSpan FineNudge =
        TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan CoarseNudge =
        TimeSpan.FromSeconds(1);
    private readonly TrimViewModel _viewModel;

    public TrimWindow(TrimViewModel viewModel)
    {
        _viewModel =
            viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        Waveform.Snapshot = viewModel.Snapshot;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        LoadAlbumArt();
    }

    protected override void OnClosed(EventArgs args)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
        base.OnClosed(args);
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args
    )
    {
        switch (args.PropertyName)
        {
            case nameof(TrimViewModel.SelectionStart):
                Waveform.SelectionStart =
                    _viewModel.SelectionStart;
                break;
            case nameof(TrimViewModel.SelectionEnd):
                Waveform.SelectionEnd = _viewModel.SelectionEnd;
                break;
            case nameof(TrimViewModel.Playhead):
                Waveform.Playhead = _viewModel.Playhead;
                break;
        }
    }

    private void OnWaveformSelectionChanged(
        object sender,
        WaveformSelectionChangedEventArgs args
    )
    {
        if (args.Start is { } start && args.End is { } end)
        {
            _viewModel.SetSelection(start, end);
        }
        else
        {
            _viewModel.ClearSelection();
        }
    }

    private void OnWaveformActiveEdgeChanged(
        object sender,
        SelectionEdgeChangedEventArgs args
    ) => _viewModel.SetActiveEdge(args.Edge);

    private void OnStartControlFocused(
        object sender,
        KeyboardFocusChangedEventArgs args
    ) => _viewModel.SetActiveEdge(SelectionEdge.Start);

    private void OnEndControlFocused(
        object sender,
        KeyboardFocusChangedEventArgs args
    ) => _viewModel.SetActiveEdge(SelectionEdge.End);

    private void OnNudgeStartEarlier(
        object sender,
        RoutedEventArgs args
    ) => _viewModel.Nudge(SelectionEdge.Start, -FineNudge);

    private void OnNudgeStartLater(
        object sender,
        RoutedEventArgs args
    ) => _viewModel.Nudge(SelectionEdge.Start, FineNudge);

    private void OnNudgeEndEarlier(
        object sender,
        RoutedEventArgs args
    ) => _viewModel.Nudge(SelectionEdge.End, -FineNudge);

    private void OnNudgeEndLater(
        object sender,
        RoutedEventArgs args
    ) => _viewModel.Nudge(SelectionEdge.End, FineNudge);

    private void OnPreviewClick(
        object sender,
        RoutedEventArgs args
    ) => _viewModel.TogglePreview();

    private void OnEditEffectPresetClick(
        object sender,
        RoutedEventArgs args
    )
    {
        if (
            sender
            is System.Windows.Controls.Button
            {
                Tag: EditEffectPreset preset,
            }
        )
        {
            _viewModel.ApplyEditEffectPreset(preset);
        }
    }

    private void OnEqualizerPresetClick(
        object sender,
        RoutedEventArgs args
    )
    {
        if (
            sender
            is System.Windows.Controls.Button
            {
                Tag: EqualizerPreset preset,
            }
        )
        {
            _viewModel.ApplyEqualizerPreset(preset);
        }
    }

    private void OnToggleEqualizerClick(
        object sender,
        RoutedEventArgs args
    ) => _viewModel.ToggleEqualizerExpanded();

    private async void OnIsolateStemsClick(
        object sender,
        RoutedEventArgs args
    )
    {
        if (
            _viewModel.SelectionStart is { } start
            && _viewModel.SelectionEnd is { } end
            && end - start
                > OnnxStemSeparator.MaximumSelectionDuration
        )
        {
            await _viewModel.IsolateStemsAsync(
                downloadModel: false
            );
            return;
        }

        var available =
            await _viewModel.CheckStemModelAvailableAsync();
        if (available is null)
        {
            return;
        }

        if (!available.Value)
        {
            var model = _viewModel.SelectedStemModel;
            var response = System.Windows.MessageBox.Show(
                this,
                string.Format(
                    CultureInfo.CurrentCulture,
                    AppStrings.DownloadStemModelPrompt,
                    model.DisplayName,
                    model.DisplayDownloadSize
                ),
                AppStrings.DownloadStemModelTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Information
            );
            if (response != MessageBoxResult.Yes)
            {
                return;
            }
        }

        await _viewModel.IsolateStemsAsync(
            downloadModel: !available.Value
        );
    }

    private void OnCancelStemIsolationClick(
        object sender,
        RoutedEventArgs args
    ) => _viewModel.CancelStemIsolation();

    private void OnShowStemSlidersClick(
        object sender,
        RoutedEventArgs args
    ) => _viewModel.SetStemBandView(isBandView: false);

    private void OnShowStemBandClick(
        object sender,
        RoutedEventArgs args
    ) => _viewModel.SetStemBandView(isBandView: true);

    private async void OnExportClick(
        object sender,
        RoutedEventArgs args
    ) => await _viewModel.ExportAsync();

    private void OnChangeFolderClick(
        object sender,
        RoutedEventArgs args
    )
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = AppStrings.ChooseOutputFolder,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = _viewModel.OutputFolder,
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            _viewModel.TrySetOutputFolder(
                dialog.SelectedPath,
                out _
            );
        }
    }

    private void OnCloseClick(
        object sender,
        RoutedEventArgs args
    ) => Close();

    private void OnDismissSpotifyHintClick(
        object sender,
        RoutedEventArgs args
    ) => _viewModel.DismissSpotifyLocalFilesHint();

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

    private void OnPreviewKeyDown(
        object sender,
        KeyEventArgs args
    )
    {
        if (args.Key == Key.Escape)
        {
            args.Handled = true;
            Close();
            return;
        }

        if (args.Key is not (Key.Left or Key.Right))
        {
            return;
        }

        var nudge =
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                ? CoarseNudge
                : FineNudge;
        if (args.Key == Key.Left)
        {
            nudge = -nudge;
        }

        _viewModel.NudgeActiveEdge(nudge);
        args.Handled = true;
    }

    private void LoadAlbumArt()
    {
        if (_viewModel.Session.Track.AlbumArt.IsEmpty)
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream(
                _viewModel.Session.Track.AlbumArt.ToArray(),
                writable: false
            );
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            AlbumArtImage.Source = image;
            AlbumArtPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
            when (exception is NotSupportedException
                or IOException
                or InvalidOperationException)
        {
            AlbumArtImage.Source = null;
            AlbumArtPlaceholder.Visibility = Visibility.Visible;
        }
    }
}
