using System.IO;
using System.IO.Compression;
using System.Text;

namespace Hookline.App.Tests;

public sealed class SpotifyLocalFilesSourceDetectorTests
    : IDisposable
{
    private readonly string _temporaryDirectory;
    private readonly string _spotifyUsersFolder;

    public SpotifyLocalFilesSourceDetectorTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hookline-spotify-sources-{Guid.NewGuid():N}"
        );
        _spotifyUsersFolder = Path.Combine(
            _temporaryDirectory,
            "Spotify",
            "Users"
        );
        Directory.CreateDirectory(_spotifyUsersFolder);
    }

    [Fact]
    public void ParsesCurrentCompressedWatchSourcesShape()
    {
        var sourceFolder = CreateDirectory(
            "Library",
            "Local music"
        );
        var account = CreateAccount("account-a");
        WriteBank(account, BuildDirectoryPayload(sourceFolder));
        var detector = new SpotifyLocalFilesSourceDetector(
            _spotifyUsersFolder,
            [sourceFolder]
        );

        var detected = detector.DetectSourceFolder();

        Assert.Equal(sourceFolder, detected);
    }

    [Fact]
    public void EmptyAndMalformedBanksFallBackWithoutThrowing()
    {
        var candidate = CreateDirectory("Candidate");
        var emptyAccount = CreateAccount("empty");
        File.WriteAllBytes(
            Path.Combine(emptyAccount, "watch-sources.bnk"),
            []
        );
        var malformedAccount = CreateAccount("malformed");
        File.WriteAllBytes(
            Path.Combine(
                malformedAccount,
                "watch-sources.bnk"
            ),
            "SPCOWatchSources-not-gzip"u8.ToArray()
        );
        Directory.SetLastWriteTimeUtc(
            malformedAccount,
            DateTime.UtcNow.AddMinutes(1)
        );
        var detector = new SpotifyLocalFilesSourceDetector(
            _spotifyUsersFolder,
            [candidate]
        );

        var exception = Record.Exception(
            () => detector.DetectSourceFolder()
        );

        Assert.Null(exception);
        Assert.Null(detector.DetectSourceFolder());
    }

    [Fact]
    public void MostRecentlyModifiedAccountWins()
    {
        var oldSource = CreateDirectory("Old source");
        var newSource = CreateDirectory("New source");
        var oldAccount = CreateAccount("old-account");
        var newAccount = CreateAccount("new-account");
        WriteBank(
            oldAccount,
            BuildDirectoryPayload(oldSource)
        );
        WriteBank(
            newAccount,
            BuildDirectoryPayload(newSource)
        );
        Directory.SetLastWriteTimeUtc(
            oldAccount,
            DateTime.UtcNow.AddHours(-1)
        );
        Directory.SetLastWriteTimeUtc(
            newAccount,
            DateTime.UtcNow
        );
        var detector = new SpotifyLocalFilesSourceDetector(
            _spotifyUsersFolder,
            [oldSource, newSource]
        );

        Assert.Equal(
            newSource,
            detector.DetectSourceFolder()
        );
    }

    [Fact]
    public void IndexedLocalFileProvidesSafeCustomSourceFallback()
    {
        var standardCandidate = CreateDirectory("Standard");
        var customSource = CreateDirectory(
            "External library",
            "Existing clips"
        );
        var account = CreateAccount("account-a");
        WriteBank(account, [0x10, 0x0A]);
        WriteLocalFilesBank(
            account,
            Path.Combine(customSource, "existing.mp3")
        );
        var detector = new SpotifyLocalFilesSourceDetector(
            _spotifyUsersFolder,
            [standardCandidate]
        );

        Assert.Equal(
            customSource,
            detector.DetectSourceFolder()
        );
    }

    public void Dispose() =>
        Directory.Delete(_temporaryDirectory, recursive: true);

    private string CreateAccount(string name) =>
        CreateDirectory("Spotify", "Users", name);

    private string CreateDirectory(params string[] components)
    {
        var path = components.Aggregate(
            _temporaryDirectory,
            Path.Combine
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] BuildDirectoryPayload(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var components = new[]
            {
                root.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                ),
            }
            .Concat(
                fullPath[root.Length..].Split(
                    [
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar,
                    ],
                    StringSplitOptions.RemoveEmptyEntries
                )
            );
        using var payload = new MemoryStream();
        payload.Write([0x10, 0x0A]);
        foreach (var component in components)
        {
            var encoded = Encoding.UTF8.GetBytes(component);
            Assert.InRange(encoded.Length, 1, byte.MaxValue);
            payload.WriteByte(0x14);
            payload.WriteByte(0x01);
            payload.WriteByte((byte)encoded.Length);
            payload.Write(encoded);
            payload.WriteByte(0x08);
            payload.WriteByte(0x00);
        }

        return payload.ToArray();
    }

    private static void WriteBank(
        string accountFolder,
        byte[] payload
    )
    {
        using var bank = new MemoryStream();
        bank.Write("SPCO"u8);
        bank.Write([0x0E, 0x00, 0x00, 0x00]);
        bank.Write("WatchSources"u8);
        using (
            var gzip = new GZipStream(
                bank,
                CompressionLevel.SmallestSize,
                leaveOpen: true
            )
        )
        {
            gzip.Write(payload);
        }

        File.WriteAllBytes(
            Path.Combine(
                accountFolder,
                "watch-sources.bnk"
            ),
            bank.ToArray()
        );
    }

    private static void WriteLocalFilesBank(
        string accountFolder,
        string indexedFile
    )
    {
        var encodedPath = Encoding.UTF8.GetBytes(indexedFile);
        Assert.InRange(encodedPath.Length, 1, byte.MaxValue);
        using var bank = new MemoryStream();
        bank.Write("SPCO"u8);
        bank.Write("LocalFilesStorage"u8);
        bank.Write([0x2C, 0x01, (byte)encodedPath.Length]);
        bank.Write(encodedPath);
        File.WriteAllBytes(
            Path.Combine(
                accountFolder,
                "local-files.bnk"
            ),
            bank.ToArray()
        );
    }
}
