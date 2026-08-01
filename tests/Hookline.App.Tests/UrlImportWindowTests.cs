using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using Hookline.App.Catalog;
using Hookline.App.Mixing;
using Hookline.Audio;
using Hookline.NowPlaying;

namespace Hookline.App.Tests;

public sealed class UrlImportWindowTests
{
    [Fact]
    public void ProgressBindingIsOneWayAndWindowInitializes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application? application = null;
            try
            {
                application = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                AddRequiredResources(application.Resources);
                using var viewModel = new UrlImportViewModel(
                    new UnusedImportService(),
                    showPersonalUseNotice: true
                );
                var window = new UrlImportWindow(viewModel);
                var progressBar = window.FindName(
                    "DownloadProgressBar"
                ) as ProgressBar;
                var binding = progressBar is null
                    ? null
                    : BindingOperations.GetBinding(
                        progressBar,
                        RangeBase.ValueProperty
                    );
                if (binding?.Mode != BindingMode.OneWay)
                {
                    throw new InvalidOperationException(
                        "Download progress must be bound one-way."
                    );
                }

                ConstructMixWindow();
                ConstructCatalogWindow();
                ConstructWorkspaceWindow();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                application?.Shutdown();
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();

        Assert.True(
            thread.Join(TimeSpan.FromSeconds(30)),
            "The URL import window smoke test did not finish."
        );
        Assert.Null(failure);
    }

    private static void AddRequiredResources(
        ResourceDictionary resources
    )
    {
        resources["WindowBackgroundBrush"] = Brush("#0D1016");
        resources["PanelBackgroundBrush"] = Brush("#151922");
        resources["MutedTextBrush"] = Brush("#9199AA");
        resources["PrimaryTextBrush"] = Brush("#F5F7FA");
        resources["AccentBrush"] = Brush("#5BE7B1");
        resources["WarningBrush"] = Brush("#FFB45C");
    }

    private static SolidColorBrush Brush(string value) =>
        new((Color)ColorConverter.ConvertFromString(value));

    private static void ConstructMixWindow()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-mix-window-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var catalog = new ClipCatalogService(
                new ClipCatalogRepository(
                    Path.Combine(temporaryDirectory, "clips.db")
                )
            );
            var settings = new OutputFolderSettings(
                Path.Combine(temporaryDirectory, "settings.json"),
                new NoSpotifySourceDetector(),
                Path.Combine(temporaryDirectory, "exports")
            );
            using var viewModel = new MixWindowViewModel(
                catalog,
                new LocalAudioFileImporter(),
                new UnusedExporter(),
                settings
            );
            _ = new MixWindow(viewModel);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static void ConstructWorkspaceWindow()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-workspace-window-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(temporaryDirectory);
        var watcher = new UnusedNowPlayingWatcher();
        var capture = new HooklineAudioCaptureService(watcher);
        try
        {
            var catalog = new ClipCatalogService(
                new ClipCatalogRepository(
                    Path.Combine(temporaryDirectory, "clips.db")
                )
            );
            var settings = new OutputFolderSettings(
                Path.Combine(temporaryDirectory, "settings.json"),
                new NoSpotifySourceDetector(),
                Path.Combine(temporaryDirectory, "exports")
            );
            var window = new WorkspaceWindow(
                () => null,
                capture,
                new LocalAudioFileImporter(),
                new UnusedImportService(),
                catalog,
                new UnusedExporter(),
                settings,
                new UnusedStemIsolationService()
            );
            if (window.FindName("HomeView") is null)
            {
                throw new InvalidOperationException(
                    "The workspace shell did not initialize."
                );
            }

            window.CloseForShutdown();
        }
        finally
        {
            capture.DisposeAsync().AsTask().GetAwaiter().GetResult();
            watcher.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static void ConstructCatalogWindow()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-catalog-window-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var catalog = new ClipCatalogService(
                new ClipCatalogRepository(
                    Path.Combine(temporaryDirectory, "clips.db")
                )
            );
            var viewModel = new ClipCatalogWindowViewModel(
                catalog,
                new CatalogAudioPlayer(Dispatcher.CurrentDispatcher),
                new UnusedRetrimLauncher()
            );
            var window = new ClipCatalogWindow(viewModel, catalog);
            var actionStyle = window.Resources[
                "CatalogActionButton"
            ] as Style;
            var actionBackground = actionStyle?.Setters
                .OfType<Setter>()
                .FirstOrDefault(
                    setter =>
                        setter.Property
                            == Control.BackgroundProperty
                )
                ?.Value as SolidColorBrush;
            if (actionBackground?.Color != Color.FromRgb(0x24, 0x2A, 0x36))
            {
                throw new InvalidOperationException(
                    "Catalog action buttons must use a dark background."
                );
            }

            var sortPicker = window.FindName("SortPicker") as ComboBox;
            if (
                sortPicker?.Background is not SolidColorBrush sortBackground
                || sortBackground.Color
                    != Color.FromRgb(0x24, 0x2A, 0x36)
                || sortPicker.Template is null
            )
            {
                throw new InvalidOperationException(
                    "The catalog sort picker must use its dark template."
                );
            }

            window.DisposeHosted();
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private sealed class UnusedImportService :
        IUrlAudioImportService
    {
        public Task<UrlVideoMetadata> ResolveAsync(
            string url,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ImportedAudioFile> ImportAsync(
            UrlVideoMetadata video,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed class UnusedExporter : IClipExporter
    {
        public Task<ClipExportResult> ExportAsync(
            AudioBufferSnapshot selection,
            ClipExportMetadata metadata,
            string outputFolder,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed class NoSpotifySourceDetector :
        ISpotifyLocalFilesSourceDetector
    {
        public string? DetectSourceFolder() => null;
    }

    private sealed class UnusedStemIsolationService :
        IStemIsolationService
    {
        public StemModelDescriptor GetModel(StemSeparationMode mode) =>
            throw new NotSupportedException();

        public Task<bool> IsModelAvailableAsync(
            StemSeparationMode mode,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task DownloadModelAsync(
            StemSeparationMode mode,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<SeparatedStemSet> SeparateAsync(
            AudioBufferSnapshot selection,
            StemSeparationMode mode,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed class UnusedNowPlayingWatcher : INowPlayingWatcher
    {
        public event EventHandler<TrackChangedEventArgs>? TrackChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<PlaybackStateChangedEventArgs>?
            PlaybackStateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<PlaybackTimelineChangedEventArgs>?
            PlaybackTimelineChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<MediaSourceChangedEventArgs>?
            SourceChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<WatcherStatusChangedEventArgs>?
            StatusChanged
        {
            add { }
            remove { }
        }

        public NowPlayingTrack? CurrentTrack => null;

        public PlaybackState PlaybackState => PlaybackState.Unavailable;

        public PlaybackTimelineSnapshot? CurrentTimeline => null;

        public MediaSourceIdentity? CurrentSource => null;

        public Task StartAsync(
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task StopAsync(
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class UnusedRetrimLauncher : IClipRetrimLauncher
    {
        public Task<ClipRetrimResult> OpenAsync(
            ClipCatalogEntry entry,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
