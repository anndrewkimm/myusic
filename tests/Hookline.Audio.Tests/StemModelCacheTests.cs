using System.Net;
using System.Security.Cryptography;

namespace Hookline.Audio.Tests;

public sealed class StemModelCacheTests
{
    [Fact]
    public async Task DownloadIsVerifiedAndReused()
    {
        var modelBytes = Enumerable
            .Range(0, 4096)
            .Select(index => (byte)(index % 251))
            .ToArray();
        var descriptor = CreateDescriptor(
            Convert.ToHexString(SHA256.HashData(modelBytes))
        );
        var handler = new ByteArrayHandler(modelBytes);
        using var client = new HttpClient(handler);
        var cacheDirectory = CreateTemporaryDirectory();
        try
        {
            using var cache = new StemModelCache(
                cacheDirectory,
                client
            );
            var progress = new RecordingProgress();

            await cache.DownloadAsync(descriptor, progress);
            await cache.DownloadAsync(descriptor, progress);

            Assert.Equal(1, handler.RequestCount);
            Assert.True(
                await cache.IsAvailableAsync(descriptor)
            );
            Assert.Equal(
                modelBytes,
                await File.ReadAllBytesAsync(
                    cache.GetModelPath(descriptor)
                )
            );
            Assert.Equal(1, progress.LastValue);
        }
        finally
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptDownloadLeavesNoPartialModel()
    {
        var descriptor = CreateDescriptor(
            new string('0', 64)
        );
        using var client = new HttpClient(
            new ByteArrayHandler([1, 2, 3, 4])
        );
        var cacheDirectory = CreateTemporaryDirectory();
        try
        {
            using var cache = new StemModelCache(
                cacheDirectory,
                client
            );

            var exception = await Assert.ThrowsAsync<
                InvalidDataException
            >(
                () => cache.DownloadAsync(descriptor)
            );

            Assert.Contains(
                "integrity",
                exception.Message,
                StringComparison.OrdinalIgnoreCase
            );
            Assert.False(
                File.Exists(cache.GetModelPath(descriptor))
            );
            Assert.False(
                File.Exists(
                    cache.GetModelPath(descriptor) + ".download"
                )
            );
        }
        finally
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    private static StemModelDescriptor CreateDescriptor(
        string sha256
    ) =>
        new()
        {
            Mode = StemSeparationMode.FourStem,
            DisplayName = "Test model",
            FileName = "test.onnx",
            DownloadUri = new Uri(
                "https://models.example/test.onnx"
            ),
            DisplayDownloadSize = "4 KB",
            Sha256 = sha256,
            Stems = [StemKind.Vocals],
        };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"hookline-stem-cache-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ByteArrayHandler(byte[] bytes)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentLength = bytes.Length;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content,
                    RequestMessage = request,
                }
            );
        }
    }

    private sealed class RecordingProgress : IProgress<double>
    {
        public double LastValue { get; private set; }

        public void Report(double value) => LastValue = value;
    }
}
