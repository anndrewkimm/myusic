using System.IO;

namespace Hookline.App.Tests;

public sealed class OutputFolderSettingsTests
{
    [Fact]
    public void ConfiguredFolderPersistsAcrossInstances()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-settings-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var settingsPath = Path.Combine(
                temporaryDirectory,
                "settings.json"
            );
            var outputPath = Path.Combine(
                temporaryDirectory,
                "Clips"
            );

            var first = new OutputFolderSettings(
                settingsPath,
                new FakeDetector()
            );
            first.SetOutputFolder(outputPath);
            var second = new OutputFolderSettings(
                settingsPath,
                new FakeDetector(
                    Path.Combine(
                        temporaryDirectory,
                        "Spotify source"
                    )
                )
            );

            Assert.Equal(
                Path.GetFullPath(outputPath),
                second.OutputFolder
            );
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void DetectedSpotifySourceBecomesFirstRunDefault()
    {
        using var fixture = new SettingsFixture();
        var sourceFolder = fixture.CreateDirectory(
            "Spotify source"
        );
        var settings = new OutputFolderSettings(
            fixture.SettingsPath,
            new FakeDetector(sourceFolder),
            fixture.CreateDirectory("Fallback")
        );

        Assert.Equal(
            Path.Combine(sourceFolder, AppStrings.AppName),
            settings.OutputFolder
        );
        Assert.False(settings.ShouldShowSpotifyLocalFilesHint);
    }

    [Fact]
    public void DedicatedHooklineSourceIsNotNestedTwice()
    {
        using var fixture = new SettingsFixture();
        var sourceFolder = fixture.CreateDirectory(
            AppStrings.AppName
        );
        var settings = new OutputFolderSettings(
            fixture.SettingsPath,
            new FakeDetector(sourceFolder)
        );

        Assert.Equal(sourceFolder, settings.OutputFolder);
    }

    [Fact]
    public void MissingSourceUsesFallbackAndDismissesHintOnce()
    {
        using var fixture = new SettingsFixture();
        var fallback = fixture.CreateDirectory("Fallback");
        var first = new OutputFolderSettings(
            fixture.SettingsPath,
            new FakeDetector(),
            fallback
        );

        Assert.Equal(fallback, first.OutputFolder);
        Assert.True(first.ShouldShowSpotifyLocalFilesHint);
        first.DismissSpotifyLocalFilesHint();

        var second = new OutputFolderSettings(
            fixture.SettingsPath,
            new FakeDetector(),
            fallback
        );
        Assert.Equal(fallback, second.OutputFolder);
        Assert.False(second.ShouldShowSpotifyLocalFilesHint);

        var newSource = fixture.CreateDirectory("Later source");
        var afterSpotifyRestart = new OutputFolderSettings(
            fixture.SettingsPath,
            new FakeDetector(newSource),
            fallback
        );
        Assert.Equal(
            Path.Combine(newSource, AppStrings.AppName),
            afterSpotifyRestart.OutputFolder
        );
        Assert.False(
            afterSpotifyRestart.ShouldShowSpotifyLocalFilesHint
        );
    }

    [Fact]
    public void UrlImportNoticeIsShownOnlyOnce()
    {
        using var fixture = new SettingsFixture();
        var first = new OutputFolderSettings(
            fixture.SettingsPath,
            new FakeDetector()
        );

        Assert.True(first.ShouldShowUrlImportNotice);
        first.MarkUrlImportNoticeShown();

        var second = new OutputFolderSettings(
            fixture.SettingsPath,
            new FakeDetector()
        );
        Assert.False(second.ShouldShowUrlImportNotice);

        second.DismissSpotifyLocalFilesHint();
        var afterOtherSettingChange = new OutputFolderSettings(
            fixture.SettingsPath,
            new FakeDetector()
        );
        Assert.False(
            afterOtherSettingChange.ShouldShowUrlImportNotice
        );
    }

    private sealed class SettingsFixture : IDisposable
    {
        private readonly string _temporaryDirectory;

        public SettingsFixture()
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                $"hookline-output-settings-{Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(_temporaryDirectory);
            SettingsPath = Path.Combine(
                _temporaryDirectory,
                "settings.json"
            );
        }

        public string SettingsPath { get; }

        public string CreateDirectory(string name)
        {
            var path = Path.Combine(_temporaryDirectory, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose() =>
            Directory.Delete(
                _temporaryDirectory,
                recursive: true
            );
    }

    private sealed class FakeDetector(string? sourceFolder = null)
        : ISpotifyLocalFilesSourceDetector
    {
        public string? DetectSourceFolder() => sourceFolder;
    }
}
