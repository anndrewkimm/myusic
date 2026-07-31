using System.Net;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace Hookline.Audio;

public sealed class YoutubeVideoAudioSource :
    IVideoAudioSource,
    IDisposable
{
    private const int MaximumThumbnailBytes = 16 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly YoutubeClient _youtube;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public YoutubeVideoAudioSource()
        : this(new HttpClient(), ownsHttpClient: true)
    {
    }

    internal YoutubeVideoAudioSource(
        HttpClient httpClient,
        bool ownsHttpClient = false
    )
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _youtube = new YoutubeClient(httpClient);
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<UrlVideoMetadata> ResolveAsync(
        Uri videoUrl,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(videoUrl);
        ThrowIfDisposed();
        var videoId = VideoId.TryParse(videoUrl.AbsoluteUri);
        if (!videoId.HasValue)
        {
            throw new UrlAudioImportException(
                UrlAudioImportFailure.InvalidUrl,
                AudioStrings.UrlImportInvalidUrl
            );
        }

        try
        {
            var video = await _youtube.Videos.GetAsync(
                videoId.Value,
                cancellationToken
            );
            var manifest = await _youtube.Videos.Streams
                .GetManifestAsync(videoId.Value, cancellationToken);
            var stream = SelectPreferredStream(manifest);
            var thumbnail = await TryDownloadThumbnailAsync(
                video.Thumbnails.TryGetWithHighestResolution()?.Url,
                cancellationToken
            );
            return new UrlVideoMetadata
            {
                SourceUrl = videoUrl,
                VideoId = video.Id.Value,
                Title = video.Title?.Trim() ?? string.Empty,
                Channel =
                    video.Author.ChannelTitle?.Trim()
                    ?? string.Empty,
                Duration = video.Duration ?? TimeSpan.Zero,
                AudioFileExtension = GetFileExtension(stream),
                Thumbnail = thumbnail,
            };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new UrlAudioImportException(
                UrlAudioImportFailure.Network,
                AudioStrings.UrlImportNetworkFailed,
                exception
            );
        }
        catch (HttpRequestException exception)
        {
            throw new UrlAudioImportException(
                UrlAudioImportFailure.Network,
                AudioStrings.UrlImportNetworkFailed,
                exception
            );
        }
        catch (YoutubeExplodeException exception)
        {
            throw MapYoutubeFailure(exception);
        }
    }

    public async Task DownloadAudioAsync(
        UrlVideoMetadata video,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ThrowIfDisposed();

        try
        {
            var manifest = await _youtube.Videos.Streams
                .GetManifestAsync(video.VideoId, cancellationToken);
            var stream = SelectStreamForExtension(
                manifest,
                video.AudioFileExtension
            );
            await _youtube.Videos.Streams.DownloadAsync(
                stream,
                destinationPath,
                progress,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new UrlAudioImportException(
                UrlAudioImportFailure.Network,
                AudioStrings.UrlImportNetworkFailed,
                exception
            );
        }
        catch (HttpRequestException exception)
        {
            throw new UrlAudioImportException(
                UrlAudioImportFailure.Network,
                AudioStrings.UrlImportNetworkFailed,
                exception
            );
        }
        catch (YoutubeExplodeException exception)
        {
            throw MapYoutubeFailure(exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        _disposed = true;
    }

    private static IStreamInfo SelectPreferredStream(
        StreamManifest manifest
    )
    {
        var audioStreams = manifest.GetAudioOnlyStreams().ToArray();
        if (audioStreams.Length == 0)
        {
            throw new UrlAudioImportException(
                UrlAudioImportFailure.NoAudio,
                AudioStrings.UrlImportNoAudio
            );
        }

        var mp4 = audioStreams
            .Where(stream => stream.Container == Container.Mp4)
            .TryGetWithHighestBitrate();
        if (mp4 is not null)
        {
            return mp4;
        }

        var webm = audioStreams
            .Where(stream => stream.Container == Container.WebM)
            .TryGetWithHighestBitrate();
        if (webm is not null)
        {
            return webm;
        }

        throw new UrlAudioImportException(
            UrlAudioImportFailure.UnsupportedAudio,
            AudioStrings.UrlImportNoAudio
        );
    }

    private static IStreamInfo SelectStreamForExtension(
        StreamManifest manifest,
        string extension
    )
    {
        var container = extension.Equals(
            ".m4a",
            StringComparison.OrdinalIgnoreCase
        )
            ? Container.Mp4
            : extension.Equals(
                ".webm",
                StringComparison.OrdinalIgnoreCase
            )
                ? Container.WebM
                : throw new UrlAudioImportException(
                    UrlAudioImportFailure.UnsupportedAudio,
                    AudioStrings.UrlImportNoAudio
                );
        return manifest
                .GetAudioOnlyStreams()
                .Where(stream => stream.Container == container)
                .TryGetWithHighestBitrate()
            ?? throw new UrlAudioImportException(
                UrlAudioImportFailure.NoAudio,
                AudioStrings.UrlImportNoAudio
            );
    }

    private static string GetFileExtension(IStreamInfo stream) =>
        stream.Container == Container.Mp4 ? ".m4a" : ".webm";

    private async Task<ReadOnlyMemory<byte>> TryDownloadThumbnailAsync(
        string? thumbnailUrl,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        try
        {
            using var response = await _httpClient.GetAsync(
                thumbnailUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            if (
                response.StatusCode != HttpStatusCode.OK
                || response.Content.Headers.ContentLength
                    is > MaximumThumbnailBytes
            )
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            await using var source = await response.Content
                .ReadAsStreamAsync(cancellationToken);
            using var destination = new MemoryStream();
            var buffer = new byte[32 * 1024];
            while (true)
            {
                var read = await source.ReadAsync(
                    buffer,
                    cancellationToken
                );
                if (read == 0)
                {
                    break;
                }

                if (destination.Length + read > MaximumThumbnailBytes)
                {
                    return ReadOnlyMemory<byte>.Empty;
                }

                destination.Write(buffer, 0, read);
            }

            return destination.ToArray();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException
                or IOException
                or OperationCanceledException)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    private static UrlAudioImportException MapYoutubeFailure(
        YoutubeExplodeException exception
    ) => exception is VideoUnavailableException
        or VideoUnplayableException
        or VideoRequiresPurchaseException
            ? new UrlAudioImportException(
                UrlAudioImportFailure.VideoUnavailable,
                AudioStrings.UrlImportVideoUnavailable,
                exception
            )
            : new UrlAudioImportException(
                UrlAudioImportFailure.FetchFailed,
                AudioStrings.UrlImportFetchFailed,
                exception
            );

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
