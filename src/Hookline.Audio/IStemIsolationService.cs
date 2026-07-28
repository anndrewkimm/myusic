namespace Hookline.Audio;

public interface IStemIsolationService
{
    StemModelDescriptor GetModel(StemSeparationMode mode);

    Task<bool> IsModelAvailableAsync(
        StemSeparationMode mode,
        CancellationToken cancellationToken = default
    );

    Task DownloadModelAsync(
        StemSeparationMode mode,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    );

    Task<SeparatedStemSet> SeparateAsync(
        AudioBufferSnapshot selection,
        StemSeparationMode mode,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    );
}
