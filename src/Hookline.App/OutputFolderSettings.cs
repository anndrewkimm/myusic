using System.IO;
using System.Text.Json;

namespace Hookline.App;

public sealed class OutputFolderSettings
{
    private readonly string _settingsPath;
    private string? _explicitOutputFolder;
    private bool _spotifyHintDismissed;
    private bool _urlImportNoticeShown;

    public OutputFolderSettings()
        : this(
            GetSettingsPath(),
            new SpotifyLocalFilesSourceDetector()
        )
    {
    }

    public OutputFolderSettings(string settingsPath)
        : this(
            settingsPath,
            new SpotifyLocalFilesSourceDetector()
        )
    {
    }

    public OutputFolderSettings(
        string settingsPath,
        ISpotifyLocalFilesSourceDetector detector,
        string? fallbackOutputFolder = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentNullException.ThrowIfNull(detector);
        _settingsPath = Path.GetFullPath(settingsPath);

        var document = Load();
        _explicitOutputFolder = Normalize(
            document?.OutputFolder
        );
        _spotifyHintDismissed =
            document?.SpotifyLocalFilesHintDismissed ?? false;
        _urlImportNoticeShown =
            document?.UrlImportPersonalUseNoticeShown ?? false;

        string? spotifySource = null;
        if (_explicitOutputFolder is null)
        {
            try
            {
                spotifySource = detector.DetectSourceFolder();
            }
            catch
            {
                // Detection is optional and must never block app startup.
            }
        }

        OutputFolder =
            _explicitOutputFolder
            ?? BuildSpotifyOutputFolder(spotifySource)
            ?? Normalize(fallbackOutputFolder)
            ?? GetDefaultOutputFolder();
        ShouldShowSpotifyLocalFilesHint =
            _explicitOutputFolder is null
            && spotifySource is null
            && !_spotifyHintDismissed;
    }

    public string OutputFolder { get; private set; }

    public bool ShouldShowSpotifyLocalFilesHint
    {
        get;
        private set;
    }

    public bool ShouldShowUrlImportNotice =>
        !_urlImportNoticeShown;

    public void SetOutputFolder(string outputFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFolder);
        var fullPath = Path.GetFullPath(outputFolder);
        Save(
            new SettingsDocument
            {
                OutputFolder = fullPath,
                SpotifyLocalFilesHintDismissed = true,
                UrlImportPersonalUseNoticeShown =
                    _urlImportNoticeShown,
            }
        );
        _explicitOutputFolder = fullPath;
        _spotifyHintDismissed = true;
        OutputFolder = fullPath;
        ShouldShowSpotifyLocalFilesHint = false;
    }

    public void DismissSpotifyLocalFilesHint()
    {
        Save(
            new SettingsDocument
            {
                OutputFolder = _explicitOutputFolder,
                SpotifyLocalFilesHintDismissed = true,
                UrlImportPersonalUseNoticeShown =
                    _urlImportNoticeShown,
            }
        );
        _spotifyHintDismissed = true;
        ShouldShowSpotifyLocalFilesHint = false;
    }

    public void MarkUrlImportNoticeShown()
    {
        Save(
            new SettingsDocument
            {
                OutputFolder = _explicitOutputFolder,
                SpotifyLocalFilesHintDismissed =
                    _spotifyHintDismissed,
                UrlImportPersonalUseNoticeShown = true,
            }
        );
        _urlImportNoticeShown = true;
    }

    private SettingsDocument? Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return null;
            }

            var document = JsonSerializer.Deserialize<SettingsDocument>(
                File.ReadAllText(_settingsPath)
            );
            return document;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Save(SettingsDocument document)
    {
        var parent = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var json = JsonSerializer.Serialize(
            document,
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(_settingsPath, json);
    }

    private static string GetSettingsPath()
    {
        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        return Path.Combine(
            localData,
            AppStrings.AppName,
            "settings.json"
        );
    }

    private static string GetDefaultOutputFolder()
    {
        var music = Environment.GetFolderPath(
            Environment.SpecialFolder.MyMusic
        );
        if (!string.IsNullOrWhiteSpace(music))
        {
            return Path.Combine(music, AppStrings.AppName);
        }

        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        return Path.Combine(
            localData,
            AppStrings.AppName,
            "Exports"
        );
    }

    private static string? BuildSpotifyOutputFolder(
        string? spotifySource
    )
    {
        var normalized = Normalize(spotifySource);
        if (normalized is null)
        {
            return null;
        }

        return string.Equals(
            Path.GetFileName(
                normalized.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                )
            ),
            AppStrings.AppName,
            StringComparison.OrdinalIgnoreCase
        )
            ? normalized
            : Path.Combine(normalized, AppStrings.AppName);
    }

    private static string? Normalize(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(path);

    private sealed record SettingsDocument
    {
        public string? OutputFolder { get; init; }

        public bool SpotifyLocalFilesHintDismissed { get; init; }

        public bool UrlImportPersonalUseNoticeShown { get; init; }
    }
}
