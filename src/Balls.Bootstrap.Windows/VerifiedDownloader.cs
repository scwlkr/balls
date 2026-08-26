using System.Buffers;
using System.Security.Cryptography;

namespace Balls.Bootstrap.Windows;

internal sealed class VerifiedDownloader : IDisposable
{
    private readonly HttpClient client;

    public VerifiedDownloader()
    {
        client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Balls-Bootstrap/1");
    }

    public async Task<byte[]> DownloadBytesAsync(
        Uri uri,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
        {
            throw new InvalidDataException("A Balls download exceeded its allowed size.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var result = new MemoryStream(capacity: Math.Min(maximumBytes, 65_536));
        var buffer = ArrayPool<byte>.Shared.Rent(16_384);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                if (result.Length + read > maximumBytes)
                {
                    throw new InvalidDataException("A Balls download exceeded its allowed size.");
                }
                await result.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            return result.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async Task DownloadVerifiedAssetAsync(
        ReleaseAsset asset,
        string destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            asset.Url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
        {
            throw new InvalidDataException($"The Balls asset is too large: {asset.Name}");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 65_536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(65_536);
        long total = 0;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                total = checked(total + read);
                if (total > maximumBytes)
                {
                    throw new InvalidDataException($"The Balls asset is too large: {asset.Name}");
                }
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(actual, asset.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"SHA-256 verification failed for {asset.Name}.");
        }
    }

    public void Dispose() => client.Dispose();
}
