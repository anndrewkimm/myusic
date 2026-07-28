using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Hookline.App;

public sealed class SpotifyLocalFilesSourceDetector
    : ISpotifyLocalFilesSourceDetector
{
    private const int MaximumBankBytes = 16 * 1024 * 1024;
    private const int MaximumPayloadBytes = 64 * 1024 * 1024;
    private static readonly byte[] BankMagic = "SPCO"u8.ToArray();
    private static readonly byte[] PayloadName =
        "WatchSources"u8.ToArray();
    private static readonly byte[] LocalFilesPayloadName =
        "LocalFilesStorage"u8.ToArray();
    private static readonly byte[] GzipMagic = [0x1F, 0x8B, 0x08];
    private static readonly UTF8Encoding StrictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true
        );

    private readonly string _spotifyUsersFolder;
    private readonly string[] _candidateFolders;

    public SpotifyLocalFilesSourceDetector()
        : this(
            GetSpotifyUsersFolder(),
            GetDefaultCandidates()
        )
    {
    }

    public SpotifyLocalFilesSourceDetector(
        string spotifyUsersFolder,
        IEnumerable<string> candidateFolders
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            spotifyUsersFolder
        );
        ArgumentNullException.ThrowIfNull(candidateFolders);

        _spotifyUsersFolder = Path.GetFullPath(
            spotifyUsersFolder
        );
        _candidateFolders = candidateFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string? DetectSourceFolder()
    {
        try
        {
            if (!Directory.Exists(_spotifyUsersFolder))
            {
                return null;
            }

            var accountFolder = new DirectoryInfo(
                    _spotifyUsersFolder
                )
                .EnumerateDirectories()
                .OrderByDescending(
                    folder => folder.LastWriteTimeUtc
                )
                .FirstOrDefault();
            if (accountFolder is null)
            {
                return null;
            }

            var bankPath = Path.Combine(
                accountFolder.FullName,
                "watch-sources.bnk"
            );
            var payload = TryReadPayload(
                bankPath,
                PayloadName,
                requireCompression: true
            );
            if (payload is not null)
            {
                var encodedDirectories = ReadDirectoryNames(payload);
                foreach (var candidate in _candidateFolders)
                {
                    if (
                        Directory.Exists(candidate)
                        && (
                            ContainsEncodedPath(
                                encodedDirectories,
                                candidate
                            )
                            || ContainsDirectPath(payload, candidate)
                        )
                    )
                    {
                        return candidate;
                    }
                }
            }

            return TryDetectIndexedLocalFileFolder(
                accountFolder.FullName
            );
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or SecurityException
                or InvalidDataException
                or ArgumentException
                or NotSupportedException)
        {
        }

        return null;
    }

    private static byte[]? TryReadPayload(
        string bankPath,
        ReadOnlySpan<byte> payloadName,
        bool requireCompression
    )
    {
        if (!File.Exists(bankPath))
        {
            return null;
        }

        var bankInfo = new FileInfo(bankPath);
        if (
            bankInfo.Length <= 0
            || bankInfo.Length > MaximumBankBytes
        )
        {
            return null;
        }

        var bank = File.ReadAllBytes(bankPath);
        if (
            !bank.AsSpan().StartsWith(BankMagic)
            || bank.AsSpan().IndexOf(payloadName) < 0
        )
        {
            return null;
        }

        var gzipOffset = bank.AsSpan().IndexOf(GzipMagic);
        if (gzipOffset < 0)
        {
            return requireCompression ? null : bank;
        }

        using var compressed = new MemoryStream(
            bank,
            gzipOffset,
            bank.Length - gzipOffset,
            writable: false
        );
        using var gzip = new GZipStream(
            compressed,
            CompressionMode.Decompress
        );
        using var payload = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = gzip.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            if (payload.Length + read > MaximumPayloadBytes)
            {
                return null;
            }

            payload.Write(buffer, 0, read);
        }

        return payload.ToArray();
    }

    private static string? TryDetectIndexedLocalFileFolder(
        string accountFolder
    )
    {
        var payload = TryReadPayload(
            Path.Combine(accountFolder, "local-files.bnk"),
            LocalFilesPayloadName,
            requireCompression: false
        );
        if (payload is null)
        {
            return null;
        }

        foreach (var path in ReadIndexedFilePaths(payload))
        {
            if (!Path.IsPathFullyQualified(path))
            {
                continue;
            }

            var folder = Path.GetDirectoryName(path);
            if (
                !string.IsNullOrWhiteSpace(folder)
                && Directory.Exists(folder)
            )
            {
                // This may be below the configured source root, but it is
                // guaranteed to be in a tree Spotify already indexed.
                return Path.GetFullPath(folder);
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ReadIndexedFilePaths(
        ReadOnlySpan<byte> payload
    )
    {
        var paths = new List<string>();
        for (var index = 0; index < payload.Length - 4; index++)
        {
            if (
                payload[index] != 0x2C
                || payload[index + 1] != 0x01
            )
            {
                continue;
            }

            var byteLength = payload[index + 2];
            var pathStart = index + 3;
            if (
                byteLength == 0
                || pathStart + byteLength > payload.Length
            )
            {
                continue;
            }

            try
            {
                paths.Add(
                    StrictUtf8.GetString(
                        payload.Slice(pathStart, byteLength)
                    )
                );
                index = pathStart + byteLength - 1;
            }
            catch (DecoderFallbackException)
            {
            }
        }

        return paths;
    }

    private static IReadOnlyList<string> ReadDirectoryNames(
        ReadOnlySpan<byte> payload
    )
    {
        var names = new List<string>();
        for (var index = 0; index < payload.Length - 3; index++)
        {
            if (payload[index] != 0x01)
            {
                continue;
            }

            var byteLength = payload[index + 1];
            var nameStart = index + 2;
            var markerIndex = nameStart + byteLength;
            if (
                byteLength == 0
                || markerIndex >= payload.Length
                || payload[markerIndex] != 0x08
            )
            {
                continue;
            }

            try
            {
                names.Add(
                    StrictUtf8.GetString(
                        payload.Slice(nameStart, byteLength)
                    )
                );
                index = markerIndex;
            }
            catch (DecoderFallbackException)
            {
            }
        }

        return names;
    }

    private static bool ContainsEncodedPath(
        IReadOnlyList<string> encodedDirectories,
        string candidate
    )
    {
        var components = SplitPath(candidate);
        if (components.Count == 0)
        {
            return false;
        }

        var componentIndex = 0;
        foreach (var encodedDirectory in encodedDirectories)
        {
            if (
                string.Equals(
                    encodedDirectory,
                    components[componentIndex],
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                componentIndex++;
                if (componentIndex == components.Count)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsDirectPath(
        ReadOnlySpan<byte> payload,
        string candidate
    )
    {
        var decoded = Encoding.UTF8.GetString(payload);
        return decoded.Contains(
                candidate,
                StringComparison.OrdinalIgnoreCase
            )
            || decoded.Contains(
                candidate.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static IReadOnlyList<string> SplitPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return [];
        }

        var components = new List<string>
        {
            root.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            ),
        };
        components.AddRange(
            fullPath[root.Length..].Split(
                [
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar,
                ],
                StringSplitOptions.RemoveEmptyEntries
            )
        );
        return components;
    }

    private static string GetSpotifyUsersFolder()
    {
        var roamingData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData
        );
        return Path.Combine(
            roamingData,
            "Spotify",
            "Users"
        );
    }

    private static IEnumerable<string> GetDefaultCandidates()
    {
        var profile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile
        );
        var candidates = new[]
        {
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyMusic
            ),
            string.IsNullOrWhiteSpace(profile)
                ? string.Empty
                : Path.Combine(profile, "Downloads"),
            Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory
            ),
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments
            ),
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyVideos
            ),
        };
        return candidates.Where(
            candidate => !string.IsNullOrWhiteSpace(candidate)
        );
    }
}
