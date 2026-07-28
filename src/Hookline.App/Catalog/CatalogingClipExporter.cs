using System.Globalization;
using Hookline.Audio;

namespace Hookline.App.Catalog;

public sealed class CatalogingClipExporter : IClipExporter
{
    private readonly IClipExporter _inner;
    private readonly ClipCatalogService _catalog;
    private readonly IClipFileOperations _files;

    public CatalogingClipExporter(
        IClipExporter inner,
        ClipCatalogService catalog,
        IClipFileOperations? files = null
    )
    {
        _inner =
            inner
            ?? throw new ArgumentNullException(nameof(inner));
        _catalog =
            catalog
            ?? throw new ArgumentNullException(nameof(catalog));
        _files = files ?? new SystemClipFileOperations();
    }

    public async Task<ClipExportResult> ExportAsync(
        AudioBufferSnapshot selection,
        ClipExportMetadata metadata,
        string outputFolder,
        CancellationToken cancellationToken = default
    )
    {
        var result = await _inner
            .ExportAsync(
                selection,
                metadata,
                outputFolder,
                cancellationToken
            )
            .ConfigureAwait(false);
        try
        {
            await _catalog
                .RegisterExportAsync(
                    selection,
                    metadata,
                    result,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _ = TryDelete(result.OutputPath);
            throw;
        }
        catch (Exception exception)
        {
            var cleanupException = TryDelete(result.OutputPath);
            if (cleanupException is not null)
            {
                throw new AggregateException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        AppStrings.CatalogRegistrationCleanupFailed,
                        result.OutputPath,
                        exception.Message
                    ),
                    exception,
                    cleanupException
                );
            }

            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    AppStrings.CatalogRegistrationFailed,
                    exception.Message
                ),
                exception
            );
        }
    }

    private Exception? TryDelete(string outputPath)
    {
        try
        {
            if (_files.Exists(outputPath))
            {
                _files.Delete(outputPath);
            }

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
