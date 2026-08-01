using System.Globalization;
using System.Windows;
using Hookline.App.Catalog;
using Hookline.Audio;
using Hookline.NowPlaying;

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
    private LocalAudioFileImporter? _localAudioFileImporter;
    private YoutubeVideoAudioSource? _youtubeVideoAudioSource;
    private IUrlAudioImportService? _urlAudioImportService;
    private StemIsolationService? _stemIsolationService;
    private GlobalHotkey? _hotkey;
    private TrayIcon? _trayIcon;
    private readonly ManagedWindowSlot<WorkspaceWindow>
        _workspaceWindowSlot = new();
    private bool _isExiting;

    protected override async void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);

        _localAudioFileImporter = new LocalAudioFileImporter();
        _youtubeVideoAudioSource = new YoutubeVideoAudioSource();
        _urlAudioImportService = new UrlAudioImportService(
            _youtubeVideoAudioSource,
            _localAudioFileImporter
        );
        _stemIsolationService =
            StemIsolationService.CreateDefault();
        _trayIcon = new TrayIcon(
            ShowWorkspace,
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
        ShowWorkspace();

    private void ShowWorkspace()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(ShowWorkspace);
            return;
        }

        if (_isExiting)
        {
            return;
        }

        if (
            _workspaceWindowSlot.TryActivateExisting(
                window => window.IsLoaded,
                RestoreWorkspaceWindow,
                window => window.CloseForShutdown()
            )
        )
        {
            return;
        }

        if (
            _captureService is null
            || _localAudioFileImporter is null
            || _urlAudioImportService is null
            || _catalogService is null
            || _exporter is null
            || _outputSettings is null
            || _stemIsolationService is null
        )
        {
            _trayIcon?.ShowError(AppStrings.CatalogUnavailable);
            return;
        }

        try
        {
            _workspaceWindowSlot.TryShowNew(
                () =>
                    new WorkspaceWindow(
                        CreateCurrentCaptureSession,
                        _captureService,
                        _localAudioFileImporter,
                        _urlAudioImportService,
                        _catalogService,
                        _exporter,
                        _outputSettings,
                        _stemIsolationService
                    ),
                SubscribeWindowClosed,
                ShowAndActivateWindow,
                window => window.CloseForShutdown()
            );
        }
        catch (Exception exception)
        {
            _trayIcon?.ShowError(
                string.Format(
                    CultureInfo.CurrentCulture,
                    AppStrings.WorkspaceOpenFailed,
                    exception.Message
                )
            );
        }
    }

    private TrimSession? CreateCurrentCaptureSession()
    {
        if (
            _watcher?.CurrentTrack is not { } track
            || _captureService is null
        )
        {
            return null;
        }

        return new TrimSession
        {
            Track = track,
            Snapshot = _captureService.Query(track.InstanceId),
        };
    }

    private static void RestoreWorkspaceWindow(
        WorkspaceWindow window
    )
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

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
        _workspaceWindowSlot.CloseCurrent(
            window => window.CloseForShutdown()
        );
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
        _youtubeVideoAudioSource?.Dispose();
        _youtubeVideoAudioSource = null;
        _urlAudioImportService = null;
        _startupCancellation.Dispose();
        Shutdown();
    }
}
