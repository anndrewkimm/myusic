using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.IO;
using Hookline.App.Catalog;
using Hookline.App.Mixing;
using Hookline.Audio;

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
}
