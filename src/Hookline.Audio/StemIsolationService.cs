namespace Hookline.Audio;

public sealed class StemIsolationService : IStemIsolationService, IDisposable
{
    private readonly StemModelCache _modelCache;
    private readonly OnnxStemSeparator _separator;
    private bool _disposed;

    public StemIsolationService(
        StemModelCache modelCache,
        OnnxStemSeparator? separator = null
    )
    {
        _modelCache =
            modelCache
            ?? throw new ArgumentNullException(nameof(modelCache));
        _separator = separator ?? new OnnxStemSeparator();
    }

    public static StemIsolationService CreateDefault()
    {
        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        var cacheDirectory = Path.Combine(
            localData,
            "Hookline",
            "Models"
        );
        return new StemIsolationService(
            new StemModelCache(cacheDirectory)
        );
    }

    public StemModelDescriptor GetModel(
        StemSeparationMode mode
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return StemModelCatalog.Get(mode);
    }

    public Task<bool> IsModelAvailableAsync(
        StemSeparationMode mode,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _modelCache.IsAvailableAsync(
            GetModel(mode),
            cancellationToken
        );
    }

    public Task DownloadModelAsync(
        StemSeparationMode mode,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _modelCache.DownloadAsync(
            GetModel(mode),
            progress,
            cancellationToken
        );
    }

    public async Task<SeparatedStemSet> SeparateAsync(
        AudioBufferSnapshot selection,
        StemSeparationMode mode,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var model = GetModel(mode);
        if (
            !await _modelCache.IsAvailableAsync(
                model,
                cancellationToken
            )
        )
        {
            throw new FileNotFoundException(
                AudioStrings.StemModelUnavailable
            );
        }

        return await _separator.SeparateAsync(
            selection,
            _modelCache.GetModelPath(model),
            model,
            progress,
            cancellationToken
        );
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _modelCache.Dispose();
        _disposed = true;
    }
}
