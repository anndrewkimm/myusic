using System.Buffers;
using System.Security.Cryptography;

namespace Hookline.Audio;

public sealed class StemModelCache : IDisposable
{
    private const int CopyBufferSize = 128 * 1024;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private bool _disposed;

    public StemModelCache(
        string cacheDirectory,
        HttpClient? httpClient = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public string GetModelPath(StemModelDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(descriptor);
        return Path.Combine(_cacheDirectory, descriptor.FileName);
    }

    public async Task<bool> IsAvailableAsync(
        StemModelDescriptor descriptor,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(descriptor);
        var path = GetModelPath(descriptor);
        if (!File.Exists(path))
        {
            return false;
        }

        var hash = await ComputeSha256Async(
            path,
            cancellationToken
        );
        var isValid = hash.Equals(
            descriptor.Sha256,
            StringComparison.OrdinalIgnoreCase
        );
        if (!isValid)
        {
            TryDelete(path);
        }

        return isValid;
    }

    public async Task DownloadAsync(
        StemModelDescriptor descriptor,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(descriptor);
        await _downloadGate.WaitAsync(cancellationToken);
        try
        {
            if (
                await IsAvailableAsync(
                    descriptor,
                    cancellationToken
                )
            )
            {
                progress?.Report(1);
                return;
            }

            Directory.CreateDirectory(_cacheDirectory);
            var finalPath = GetModelPath(descriptor);
            var partialPath = finalPath + ".download";
            TryDelete(partialPath);

            try
            {
                using var response = await _httpClient.GetAsync(
                    descriptor.DownloadUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );
                response.EnsureSuccessStatusCode();
                var totalBytes =
                    response.Content.Headers.ContentLength;
                {
                    await using var source =
                        await response.Content.ReadAsStreamAsync(
                            cancellationToken
                        );
                    await using var destination = new FileStream(
                        partialPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        CopyBufferSize,
                        FileOptions.Asynchronous
                            | FileOptions.SequentialScan
                    );
                    var buffer = ArrayPool<byte>.Shared.Rent(
                        CopyBufferSize
                    );
                    try
                    {
                        long received = 0;
                        while (true)
                        {
                            var read = await source.ReadAsync(
                                buffer.AsMemory(0, CopyBufferSize),
                                cancellationToken
                            );
                            if (read == 0)
                            {
                                break;
                            }

                            await destination.WriteAsync(
                                buffer.AsMemory(0, read),
                                cancellationToken
                            );
                            received += read;
                            if (totalBytes is > 0)
                            {
                                progress?.Report(
                                    Math.Clamp(
                                        received
                                            / (double)totalBytes.Value,
                                        0,
                                        0.99
                                    )
                                );
                            }
                        }

                        await destination.FlushAsync(
                            cancellationToken
                        );
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }

                var hash = await ComputeSha256Async(
                    partialPath,
                    cancellationToken
                );
                if (
                    !hash.Equals(
                        descriptor.Sha256,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new InvalidDataException(
                        AudioStrings.StemModelIntegrityFailed
                    );
                }

                File.Move(
                    partialPath,
                    finalPath,
                    overwrite: true
                );
                progress?.Report(1);
            }
            finally
            {
                TryDelete(partialPath);
            }
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _downloadGate.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        _disposed = true;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken
    )
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous
                | FileOptions.SequentialScan
        );
        var hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken
        );
        return Convert.ToHexString(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
