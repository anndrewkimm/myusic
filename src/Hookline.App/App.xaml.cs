using System.Globalization;
using System.Windows;
using Hookline.App.Catalog;
using Hookline.Audio;
using Hookline.NowPlaying;
using FileOpenDialog = Microsoft.Win32.OpenFileDialog;

namespace Hookline.App;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource _startupCancellation =
        new();
    private SpotifyNowPlayingWatcher? _watcher;
    private HooklineAudioCaptureService? _captureService;
    private IClipExporter? _exporter;
    private OutputFolderSettings? _outputSettings;
    private ClipCatalogService? _catalogService;
    private ClipRetrimLauncher? _retrimLauncher;
    private LocalAudioFileImporter? _localAudioFileImporter;
    private StemIsolationService? _stemIsolationService;
    private GlobalHotkey? _hotkey;
    private TrayIcon? _trayIcon;
    private readonly ManagedWindowSlot<TrimWindow> _trimWindowSlot =
        new();
    private readonly ManagedWindowSlot<ClipCatalogWindow>
        _catalogWindowSlot = new();
    private readonly Dictionary<
        long,
        ManagedWindowSlot<TrimWindow>
    > _importWindowSlots = [];
    private bool _isImporting;
    private bool _isExiting;

    protected override async void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);

        _localAudioFileImporter = new LocalAudioFileImporter();
        _stemIsolationService =
            StemIsolationService.CreateDefault();
        _trayIcon = new TrayIcon(
            ShowTrimWindow,
            ImportAudioFile,
            ShowCatalogWindow,
            ExitApplication
        );
        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += OnHotkeyPressed;
        if (!_hotkey.TryRegister())
        {
            _trayIcon.ShowError(AppStrings.HotkeyUnavailable);
        }

        _watcher = new SpotifyNowPlayingWatcher();
        _captureService = new HooklineAudioCaptureService(_watcher);
        _captureService.StatusChanged += OnCaptureStatusChanged;
        _outputSettings = new OutputFolderSettings();
        var repository = new ClipCatalogRepository(
            ClipCatalogPaths.GetDefaultDatabasePath()
        );
        _catalogService = new ClipCatalogService(repository);
        _exporter = new CatalogingClipExporter(
            new Mp3ClipExporter(),
            _catalogService
        );
        _retrimLauncher = new ClipRetrimLauncher(
            _captureService,
            _exporter,
            _outputSettings,
            _stemIsolationService,
            Dispatcher
        );

        try
        {
            await _catalogService.InitializeAsync(
                _startupCancellation.Token
            );
        }
        catch (OperationCanceledException)
            when (_startupCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            _trayIcon.ShowError(
                string.Format(
                    CultureInfo.CurrentCulture,
                    AppStrings.CatalogDatabaseFailed,
                    exception.Message
                )
            );
        }

        try
        {
            await _captureService.StartAsync(
                _startupCancellation.Token
            );
        }
        catch (OperationCanceledException)
            when (_startupCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _trayIcon.ShowError(
                $"{AppStrings.CaptureUnavailable} {exception.Message}"
            );
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs args) =>
        ShowTrimWindow();

    private void ShowTrimWindow()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ShowTrimWindow);
            return;
        }

        if (_isExiting)
        {
            return;
        }

        if (
            _trimWindowSlot.TryActivateExisting(
                IsWindowUsable,
                RestoreAndActivateWindow,
                CloseWindow
            )
        )
        {
            return;
        }

        if (
            _watcher?.CurrentTrack is not { } track
            || _captureService is null
            || _exporter is null
            || _outputSettings is null
        )
        {
            _trayIcon?.ShowInfo(AppStrings.NoCurrentTrack);
            return;
        }

        try
        {
            var session = new TrimSession
            {
                Track = track,
                Snapshot = _captureService.Query(track.InstanceId),
            };
            _trimWindowSlot.TryShowNew(
                () => CreateTrimWindow(session),
                SubscribeWindowClosed,
                ShowAndActivateWindow,
                CloseWindow
            );
        }
        catch (Exception exception)
        {
            _trayIcon?.ShowError(
                $"{AppStrings.OpenFailed} {exception.Message}"
            );
        }
    }

    private async void ImportAudioFile()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(ImportAudioFile);
            return;
        }

        if (_isExiting)
        {
            return;
        }

        if (_isImporting)
        {
            _trayIcon?.ShowInfo(AppStrings.ImportAlreadyRunning);
            return;
        }

        if (
            _localAudioFileImporter is null
            || _exporter is null
            || _outputSettings is null
        )
        {
            _trayIcon?.ShowError(
                string.Format(
                    CultureInfo.CurrentCulture,
                    AppStrings.ImportFailed,
                    AppStrings.CatalogUnavailable
                )
            );
            return;
        }

        var dialog = new FileOpenDialog
        {
            Title = AppStrings.ChooseAudioFile,
            Filter = AppStrings.AudioFileFilter,
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _isImporting = true;
        try
        {
            var imported = await _localAudioFileImporter.ImportAsync(
                dialog.FileName,
                _startupCancellation.Token
            );
            if (_isExiting)
            {
                return;
            }

            var session =
                ImportedAudioTrimSessionFactory.Create(imported);
            var trackInstanceId = session.Track.InstanceId;
            var slot = new ManagedWindowSlot<TrimWindow>();
            _importWindowSlots.Add(trackInstanceId, slot);
            try
            {
                slot.TryShowNew(
                    () => CreateTrimWindow(session),
                    SubscribeWindowClosed,
                    ShowAndActivateWindow,
                    CloseWindow,
                    _ =>
                        _importWindowSlots.Remove(
                            trackInstanceId
                        )
                );
            }
            catch
            {
                _importWindowSlots.Remove(trackInstanceId);
                throw;
            }
        }
        catch (OperationCanceledException)
            when (_startupCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _trayIcon?.ShowError(
                string.Format(
                    CultureInfo.CurrentCulture,
                    AppStrings.ImportFailed,
                    exception.Message
                )
            );
        }
        finally
        {
            _isImporting = false;
        }
    }

    private TrimWindow CreateTrimWindow(TrimSession session)
    {
        if (
            _exporter is null
            || _outputSettings is null
            || _stemIsolationService is null
        )
        {
            throw new InvalidOperationException(
                AppStrings.OpenFailed
            );
        }

        var preview = new AudioPreviewPlayer(Dispatcher);
        var viewModel = new TrimViewModel(
            session,
            _exporter,
            preview,
            _outputSettings,
            _stemIsolationService
        );
        return new TrimWindow(viewModel);
    }

    private static bool IsWindowUsable(Window window) =>
        window.IsLoaded && window.IsVisible;

    private static void RestoreAndActivateWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private static void ShowAndActivateWindow(Window window)
    {
        window.Show();
        window.Activate();
    }

    private static void SubscribeWindowClosed(
        Window window,
        EventHandler handler
    ) => window.Closed += handler;

    private static void CloseWindow(Window window) =>
        window.Close();

    private void OnCaptureStatusChanged(
        object? sender,
        AudioCaptureStatusChangedEventArgs args
    )
    {
        if (
            args.Status is not (
                AudioCaptureStatus.Failed
                or AudioCaptureStatus.Stalled
            )
        )
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var detail = string.IsNullOrWhiteSpace(args.Detail)
                ? AppStrings.CaptureUnavailable
                : args.Detail;
            _trayIcon?.ShowError(detail);
        });
    }

    private void ShowCatalogWindow()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(ShowCatalogWindow);
            return;
        }

        if (_isExiting)
        {
            return;
        }

        if (
            _catalogWindowSlot.TryActivateExisting(
                IsWindowUsable,
                RestoreAndActivateWindow,
                CloseWindow
            )
        )
        {
            return;
        }

        if (
            _catalogService is null
            || _retrimLauncher is null
        )
        {
            _trayIcon?.ShowError(AppStrings.CatalogUnavailable);
            return;
        }

        try
        {
            _catalogWindowSlot.TryShowNew(
                () =>
                {
                    var player = new CatalogAudioPlayer(Dispatcher);
                    var viewModel =
                        new ClipCatalogWindowViewModel(
                            _catalogService,
                            player,
                            _retrimLauncher
                        );
                    return new ClipCatalogWindow(
                        viewModel,
                        _catalogService
                    );
                },
                SubscribeWindowClosed,
                ShowAndActivateWindow,
                CloseWindow
            );
        }
        catch (Exception exception)
        {
            _trayIcon?.ShowError(
                string.Format(
                    CultureInfo.CurrentCulture,
                    AppStrings.CatalogOpenFailed,
                    exception.Message
                )
            );
        }
    }

    private async void ExitApplication()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(ExitApplication);
            return;
        }

        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _startupCancellation.Cancel();
        _trimWindowSlot.CloseCurrent(CloseWindow);
        _catalogWindowSlot.CloseCurrent(CloseWindow);
        foreach (var slot in _importWindowSlots.Values.ToArray())
        {
            slot.CloseCurrent(CloseWindow);
        }

        _importWindowSlots.Clear();
        _retrimLauncher?.CloseAll();
        _hotkey?.Dispose();
        _hotkey = null;

        if (_captureService is not null)
        {
            _captureService.StatusChanged -= OnCaptureStatusChanged;
            try
            {
                await _captureService.DisposeAsync();
            }
            catch
            {
                // Shutdown must continue even if an audio backend is gone.
            }
        }

        if (_watcher is not null)
        {
            try
            {
                await _watcher.DisposeAsync();
            }
            catch
            {
                // Shutdown must continue even if Spotify already exited.
            }
        }

        _trayIcon?.Dispose();
        _trayIcon = null;
        _stemIsolationService?.Dispose();
        _stemIsolationService = null;
        _startupCancellation.Dispose();
        Shutdown();
    }
}
