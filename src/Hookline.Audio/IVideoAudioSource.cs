namespace Hookline.Audio;

public interface IVideoAudioSource
{
    Task<UrlVideoMetadata> ResolveAsync(
        Uri videoUrl,
        CancellationToken cancellationToken = default
    );

    Task DownloadAudioAsync(
        UrlVideoMetadata video,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    );
}
