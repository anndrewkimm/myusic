namespace Hookline.Audio;

public sealed class UrlAudioImportService : IUrlAudioImportService
{
    private readonly IVideoAudioSource _source;
    private readonly LocalAudioFileImporter _fileImporter;
    private readonly string _temporaryRoot;

    public UrlAudioImportService(
        IVideoAudioSource source,
        LocalAudioFileImporter fileImporter,
        string? temporaryRoot = null
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fileImporter);
        _source = source;
        _fileImporter = fileImporter;
        _temporaryRoot = Path.GetFullPath(
            temporaryRoot
                ?? Path.Combine(
                    Path.GetTempPath(),
                    "Hookline",
                    "UrlImports"
                )
        );
    }

    public async Task<UrlVideoMetadata> ResolveAsync(
        string url,
        CancellationToken cancellationToken = default
    )
    {
        var videoUrl = VideoUrlParser.Parse(url);
        var video = await _source.ResolveAsync(
            videoUrl,
            cancellationToken
        );
        if (
            video.Duration >
            LocalAudioFileImporter.DefaultMaximumDuration
        )
        {
            throw new LocalAudioImportException(
                LocalAudioImportFailure.TooLong,
                AudioStrings.ImportTooLong
            );
        }

        return video;
    }

    public async Task<ImportedAudioFile> ImportAsync(
        UrlVideoMetadata video,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(video);
        var directory = Path.Combine(
            _temporaryRoot,
            Guid.NewGuid().ToString("N")
        );
        var fileName = BuildTemporaryFileName(video);
        var temporaryPath = Path.Combine(directory, fileName);

        try
        {
            Directory.CreateDirectory(directory);
            await _source.DownloadAudioAsync(
                video,
                temporaryPath,
                progress,
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();
            var imported = await _fileImporter.ImportAsync(
                temporaryPath,
                cancellationToken
            );
            return imported with
            {
                Metadata = new ClipExportMetadata
                {
                    Title = Normalize(video.Title)
                        ?? imported.Metadata.Title,
                    Artist = Normalize(video.Channel)
                        ?? imported.Metadata.Artist,
                    Album = string.Empty,
                    AlbumArt = video.Thumbnail.IsEmpty
                        ? imported.Metadata.AlbumArt
                        : video.Thumbnail,
                },
            };
        }
        finally
        {
            TryDelete(temporaryPath);
            TryDeleteDirectory(directory);
        }
    }

    private static string BuildTemporaryFileName(
        UrlVideoMetadata video
    )
    {
        var title = Normalize(video.Title) ?? video.VideoId;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            title = title.Replace(invalid, '_');
        }

        title = title.Trim().TrimEnd('.');
        if (title.Length == 0)
        {
            title = video.VideoId;
        }

        if (title.Length > 120)
        {
            title = title[..120].TrimEnd();
        }

        var extension = video.AudioFileExtension.StartsWith('.')
            ? video.AudioFileExtension
            : $".{video.AudioFileExtension}";
        return $"{title}{extension}";
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class VideoUrlParser
{
    public static Uri Parse(string value)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(
                value.Trim(),
                UriKind.Absolute,
                out var uri
            )
            || uri.Scheme is not ("http" or "https")
            || !IsYouTubeHost(uri.Host)
        )
        {
            throw new UrlAudioImportException(
                UrlAudioImportFailure.InvalidUrl,
                AudioStrings.UrlImportInvalidUrl
            );
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (
            HasQueryParameter(uri.Query, "list")
            || segments.FirstOrDefault() is
                "playlist" or "channel" or "c" or "user"
            || segments.FirstOrDefault()?.StartsWith(
                "@",
                StringComparison.Ordinal
            ) == true
        )
        {
            throw new UrlAudioImportException(
                UrlAudioImportFailure.PlaylistOrChannel,
                AudioStrings.UrlImportSingleVideoOnly
            );
        }

        if (!YoutubeExplode.Videos.VideoId.TryParse(uri.AbsoluteUri).HasValue)
        {
            throw new UrlAudioImportException(
                UrlAudioImportFailure.InvalidUrl,
                AudioStrings.UrlImportInvalidUrl
            );
        }

        return uri;
    }

    private static bool IsYouTubeHost(string host) =>
        host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)
        || host.Equals(
            "youtube.com",
            StringComparison.OrdinalIgnoreCase
        )
        || host.EndsWith(
            ".youtube.com",
            StringComparison.OrdinalIgnoreCase
        )
        || host.Equals(
            "youtube-nocookie.com",
            StringComparison.OrdinalIgnoreCase
        )
        || host.EndsWith(
            ".youtube-nocookie.com",
            StringComparison.OrdinalIgnoreCase
        );

    private static bool HasQueryParameter(
        string query,
        string name
    ) => query
        .TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2)[0])
        .Any(
            key =>
                string.Equals(
                    Uri.UnescapeDataString(key),
                    name,
                    StringComparison.OrdinalIgnoreCase
                )
        );
}
