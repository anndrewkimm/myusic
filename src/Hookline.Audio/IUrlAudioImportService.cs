namespace Hookline.Audio;

public interface IUrlAudioImportService
{
    Task<UrlVideoMetadata> ResolveAsync(
        string url,
        CancellationToken cancellationToken = default
    );

    Task<ImportedAudioFile> ImportAsync(
        UrlVideoMetadata video,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    );
}
