using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Hookline.App.Catalog;
using Hookline.App.Mixing;
using Hookline.Audio;
using FileOpenDialog = Microsoft.Win32.OpenFileDialog;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Hookline.App;

public partial class WorkspaceWindow : Window
{
    private readonly Func<TrimSession?> _captureSessionFactory;
    private readonly HooklineAudioCaptureService _captureService;
    private readonly LocalAudioFileImporter _importer;
    private readonly IUrlAudioImportService _urlImportService;
    private readonly ClipCatalogService _catalog;
    private readonly IClipExporter _exporter;
    private readonly OutputFolderSettings _outputSettings;
    private readonly IStemIsolationService _stemIsolationService;
    private readonly CancellationTokenSource _lifetimeCancellation =
        new();
    private HostedEditor? _captureEditor;
    private HostedEditor? _activeEditor;
    private UrlImportWindow? _urlWindow;
    private FrameworkElement? _urlContent;
    private MixWindow? _mixWindow;
    private MixWindowViewModel? _mixViewModel;
    private FrameworkElement? _mixContent;
    private HostedEditor? _mixFirstEditor;
    private HostedEditor? _mixSecondEditor;
    private ClipCatalogWindow? _catalogWindow;
    private FrameworkElement? _catalogContent;
    private bool _isImporting;
    private bool _allowClose;
    private bool _disposed;

    public WorkspaceWindow(
        Func<TrimSession?> captureSessionFactory,
        HooklineAudioCaptureService captureService,
        LocalAudioFileImporter importer,
        IUrlAudioImportService urlImportService,
        ClipCatalogService catalog,
        IClipExporter exporter,
        OutputFolderSettings outputSettings,
        IStemIsolationService stemIsolationService
    )
    {
        _captureSessionFactory =
            captureSessionFactory
            ?? throw new ArgumentNullException(
                nameof(captureSessionFactory)
            );
        _captureService =
            captureService
            ?? throw new ArgumentNullException(
                nameof(captureService)
            );
        _importer =
            importer
            ?? throw new ArgumentNullException(nameof(importer));
        _urlImportService =
            urlImportService
            ?? throw new ArgumentNullException(
                nameof(urlImportService)
            );
        _catalog =
            catalog
            ?? throw new ArgumentNullException(nameof(catalog));
        _exporter =
            exporter
            ?? throw new ArgumentNullException(nameof(exporter));
        _outputSettings =
            outputSettings
            ?? throw new ArgumentNullException(
                nameof(outputSettings)
            );
        _stemIsolationService =
            stemIsolationService
            ?? throw new ArgumentNullException(
                nameof(stemIsolationService)
            );
        InitializeComponent();
        ShowHome();
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    public void ShowCaptureEditor()
    {
        try
        {
            var session = _captureSessionFactory();
            if (session is null)
            {
                HomeStatus.Text = AppStrings.NoCurrentTrack;
                ShowHome();
                return;
            }

            if (
                _captureEditor is null
                || _captureEditor.TrackInstanceId
                    != session.Track.InstanceId
            )
            {
                _captureEditor?.Dispose();
                _captureEditor = CreateEditor(session);
            }

            ShowEditor(_captureEditor, showMixContext: false);
        }
        catch (Exception exception)
        {
            HomeStatus.Text = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.OpenFailed,
                exception.Message
            );
            ShowHome();
        }
    }

    protected override void OnClosed(EventArgs args)
    {
        DisposeWorkspace();
        base.OnClosed(args);
    }

    private void OnClosing(
        object? sender,
        CancelEventArgs args
    )
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        Hide();
    }

    private void ShowHome()
    {
        _activeEditor = null;
        MixContextBar.Visibility = Visibility.Collapsed;
        HostedView.Content = null;
        HostedView.Visibility = Visibility.Collapsed;
        ImportView.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Visible;
    }

    private void ShowImport()
    {
        EnsureUrlImportView();
        _activeEditor = null;
        MixContextBar.Visibility = Visibility.Collapsed;
        HostedView.Content = null;
        HostedView.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Collapsed;
        ImportView.Visibility = Visibility.Visible;
    }

    private void ShowHosted(
        FrameworkElement content,
        bool showMixContext
    )
    {
        HomeView.Visibility = Visibility.Collapsed;
        ImportView.Visibility = Visibility.Collapsed;
        HostedView.Content = content;
        HostedView.Visibility = Visibility.Visible;
        MixContextBar.Visibility = showMixContext
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ShowEditor(
        HostedEditor editor,
        bool showMixContext
    )
    {
        _activeEditor = editor;
        ShowHosted(editor.Content, showMixContext);
    }

    private HostedEditor CreateEditor(TrimSession session)
    {
        var preview = new AudioPreviewPlayer(Dispatcher);
        var viewModel = new TrimViewModel(
            session,
            _exporter,
            preview,
            _outputSettings,
            _stemIsolationService
        );
        var window = new TrimWindow(viewModel);
        window.HostCloseRequested += (_, _) => ShowHome();
        return new HostedEditor(
            session.Track.InstanceId,
            viewModel,
            window,
            window.TakeContentForHost()
        );
    }

    private void OpenEditor(TrimSession session)
    {
        _captureEditor?.Dispose();
        _captureEditor = CreateEditor(session);
        ShowEditor(_captureEditor, showMixContext: false);
    }

    private void OpenImportedEditor(ImportedAudioFile imported) =>
        OpenEditor(ImportedAudioTrimSessionFactory.Create(imported));

    private void EnsureUrlImportView()
    {
        if (_urlWindow is not null)
        {
            return;
        }

        var showNotice = _outputSettings.ShouldShowUrlImportNotice;
        var viewModel = new UrlImportViewModel(
            _urlImportService,
            showNotice
        );
        _urlWindow = new UrlImportWindow(viewModel);
        _urlWindow.ImportCompleted += OnUrlImportCompleted;
        _urlWindow.HostCloseRequested += (_, _) => ShowHome();
        _urlContent = _urlWindow.TakeContentForHost();
        UrlImportHost.Content = _urlContent;

        if (!showNotice)
        {
            return;
        }

        try
        {
            _outputSettings.MarkUrlImportNoticeShown();
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException)
        {
            ImportStatus.Text = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.UrlImportNoticeSaveFailed,
                exception.Message
            );
        }
    }

    private async void OnUrlImportCompleted(
        object? sender,
        UrlImportCompletedEventArgs args
    )
    {
        await Dispatcher.InvokeAsync(
            () => OpenImportedEditor(args.ImportedAudio)
        );
    }

    private async Task ImportLocalAsync()
    {
        if (_isImporting)
        {
            ImportStatus.Text = AppStrings.ImportAlreadyRunning;
            return;
        }

        var dialog = new FileOpenDialog
        {
            Title = AppStrings.ChooseAudioFile,
            Filter = AppStrings.AudioFileFilter,
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _isImporting = true;
        ImportStatus.Text = AppStrings.WorkspaceImporting;
        try
        {
            var imported = await _importer.ImportAsync(
                dialog.FileName,
                _lifetimeCancellation.Token
            );
            ImportStatus.Text = string.Empty;
            OpenImportedEditor(imported);
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ImportStatus.Text = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.ImportFailed,
                exception.Message
            );
        }
        finally
        {
            _isImporting = false;
        }
    }

    private async Task ShowMixAsync()
    {
        if (_mixWindow is null)
        {
            _mixViewModel = new MixWindowViewModel(
                _catalog,
                _importer,
                _exporter,
                _outputSettings
            );
            _mixViewModel.SourceChanged += OnMixSourceChanged;
            _mixWindow = new MixWindow(_mixViewModel);
            _mixWindow.HostCloseRequested += (_, _) => ShowHome();
            _mixWindow.EditSourceRequested +=
                OnMixEditSourceRequested;
            _mixContent = _mixWindow.TakeContentForHost();
            await _mixWindow.LoadHostedAsync();
        }

        _activeEditor = null;
        ShowHosted(_mixContent!, showMixContext: false);
    }

    private void OnMixSourceChanged(
        object? sender,
        MixSourceChangedEventArgs args
    )
    {
        var baseSession =
            ImportedAudioTrimSessionFactory.Create(args.Source);
        var session = baseSession with
        {
            InitialSelectionStart = TimeSpan.Zero,
            InitialSelectionEnd = args.Source.Snapshot.Duration,
        };
        var editor = CreateEditor(session);
        if (args.Slot == MixSourceSlot.First)
        {
            UnsubscribeMixEditor(_mixFirstEditor);
            _mixFirstEditor?.Dispose();
            _mixFirstEditor = editor;
        }
        else
        {
            UnsubscribeMixEditor(_mixSecondEditor);
            _mixSecondEditor?.Dispose();
            _mixSecondEditor = editor;
        }

        editor.ViewModel.PropertyChanged +=
            OnMixEditorPropertyChanged;

        _mixViewModel?.SetSourceRenderer(
            args.Slot,
            editor.ViewModel.RenderSelectionAsync,
            () => editor.ViewModel.CanExport
        );
        ShowMixEditor(args.Slot);
    }

    private void OnMixEditorPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args
    ) => _mixViewModel?.NotifySourceRendererStateChanged();

    private void UnsubscribeMixEditor(HostedEditor? editor)
    {
        if (editor is not null)
        {
            editor.ViewModel.PropertyChanged -=
                OnMixEditorPropertyChanged;
        }
    }

    private void OnMixEditSourceRequested(
        object? sender,
        MixSourceEditRequestedEventArgs args
    ) => ShowMixEditor(args.Slot);

    private void ShowMixEditor(MixSourceSlot slot)
    {
        var editor = slot == MixSourceSlot.First
            ? _mixFirstEditor
            : _mixSecondEditor;
        if (editor is null)
        {
            _ = ShowMixAsync();
            return;
        }

        MixContextText.Text = string.Format(
            CultureInfo.CurrentCulture,
            AppStrings.WorkspaceEditingMixSource,
            slot == MixSourceSlot.First ? "A" : "B"
        );
        ShowEditor(editor, showMixContext: true);
    }

    private async Task ShowLibraryAsync()
    {
        if (_catalogWindow is null)
        {
            var player = new CatalogAudioPlayer(Dispatcher);
            var retrimLauncher = new WorkspaceClipRetrimLauncher(
                _captureService,
                _importer,
                OpenEditor
            );
            var viewModel = new ClipCatalogWindowViewModel(
                _catalog,
                player,
                retrimLauncher
            );
            _catalogWindow = new ClipCatalogWindow(
                viewModel,
                _catalog
            );
            _catalogWindow.HostCloseRequested += (_, _) => ShowHome();
            _catalogContent =
                _catalogWindow.TakeContentForHost();
            await _catalogWindow.LoadHostedAsync();
        }

        _activeEditor = null;
        ShowHosted(_catalogContent!, showMixContext: false);
    }

    private void DisposeWorkspace()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _captureEditor?.Dispose();
        UnsubscribeMixEditor(_mixFirstEditor);
        UnsubscribeMixEditor(_mixSecondEditor);
        _mixFirstEditor?.Dispose();
        _mixSecondEditor?.Dispose();
        if (_urlWindow is not null)
        {
            _urlWindow.ImportCompleted -= OnUrlImportCompleted;
            _urlWindow.CloseForShutdown();
        }

        if (_mixViewModel is not null)
        {
            _mixViewModel.SourceChanged -= OnMixSourceChanged;
        }

        if (_mixWindow is not null)
        {
            _mixWindow.EditSourceRequested -=
                OnMixEditSourceRequested;
            _mixWindow.DisposeHosted();
        }
        _catalogWindow?.DisposeHosted();
        _lifetimeCancellation.Dispose();
    }

    private void OnHomeClick(
        object sender,
        RoutedEventArgs args
    ) => ShowHome();

    private void OnCaptureClick(
        object sender,
        RoutedEventArgs args
    ) => ShowCaptureEditor();

    private void OnImportClick(
        object sender,
        RoutedEventArgs args
    ) => ShowImport();

    private async void OnImportLocalClick(
        object sender,
        RoutedEventArgs args
    ) => await ImportLocalAsync();

    private async void OnMixClick(
        object sender,
        RoutedEventArgs args
    ) => await ShowMixAsync();

    private async void OnLibraryClick(
        object sender,
        RoutedEventArgs args
    ) => await ShowLibraryAsync();

    private async void OnMixSetupClick(
        object sender,
        RoutedEventArgs args
    ) => await ShowMixAsync();

    private void OnEditMixSourceAClick(
        object sender,
        RoutedEventArgs args
    ) => ShowMixEditor(MixSourceSlot.First);

    private void OnEditMixSourceBClick(
        object sender,
        RoutedEventArgs args
    ) => ShowMixEditor(MixSourceSlot.Second);

    private void OnMinimizeClick(
        object sender,
        RoutedEventArgs args
    ) => WindowState = WindowState.Minimized;

    private void OnCloseClick(
        object sender,
        RoutedEventArgs args
    ) => Close();

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
        _activeEditor?.Window.HandleHostedPreviewKeyDown(args);
    }

    private sealed class HostedEditor : IDisposable
    {
        private bool _disposed;

        public HostedEditor(
            long trackInstanceId,
            TrimViewModel viewModel,
            TrimWindow window,
            FrameworkElement content
        )
        {
            TrackInstanceId = trackInstanceId;
            ViewModel = viewModel;
            Window = window;
            Content = content;
        }

        public long TrackInstanceId { get; }

        public TrimViewModel ViewModel { get; }

        public TrimWindow Window { get; }

        public FrameworkElement Content { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Window.DisposeHosted();
        }
    }
}
