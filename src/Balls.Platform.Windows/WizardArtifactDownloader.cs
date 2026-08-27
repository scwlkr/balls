using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Balls.Platform;

namespace Balls.Platform.Windows;

internal sealed class WizardArtifactDownloader(HttpClient client)
{
    public async Task DownloadAsync(
        BallsWizardArtifact artifact,
        string finalPath,
        IProgress<BallsWizardInstallProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        ArgumentNullException.ThrowIfNull(progress);

        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        if (await IsVerifiedAsync(artifact, finalPath, cancellationToken).ConfigureAwait(false))
        {
            progress.Report(
                new BallsWizardInstallProgress(
                    artifact.Id,
                    "verified",
                    artifact.SizeBytes,
                    artifact.SizeBytes));
            return;
        }

        if (File.Exists(finalPath))
        {
            File.Delete(finalPath);
        }

        var partialPath = finalPath + ".partial";
        var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        if (existingLength > artifact.SizeBytes)
        {
            File.Delete(partialPath);
            existingLength = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, artifact.Source);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable
            && existingLength == artifact.SizeBytes)
        {
            await VerifyAndActivateAsync(
                artifact,
                partialPath,
                finalPath,
                progress,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        response.EnsureSuccessStatusCode();
        var append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (append)
        {
            var returnedStart = response.Content.Headers.ContentRange?.From;
            if (returnedStart != existingLength)
            {
                throw new InvalidDataException("The artifact server returned an unexpected byte range.");
            }
        }
        else
        {
            existingLength = 0;
        }

        await using (var source = await response.Content
                         .ReadAsStreamAsync(cancellationToken)
                         .ConfigureAwait(false))
        await using (var destination = new FileStream(
                         partialPath,
                         append ? FileMode.Append : FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 128 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[128 * 1024];
            var downloaded = existingLength;
            while (true)
            {
                var count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                downloaded = checked(downloaded + count);
                if (downloaded > artifact.SizeBytes)
                {
                    throw new InvalidDataException("The downloaded artifact exceeded its pinned size.");
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, count),
                    cancellationToken).ConfigureAwait(false);
                progress.Report(
                    new BallsWizardInstallProgress(
                        artifact.Id,
                        "downloading",
                        downloaded,
                        artifact.SizeBytes));
            }
        }

        await VerifyAndActivateAsync(
            artifact,
            partialPath,
            finalPath,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<bool> IsVerifiedAsync(
        BallsWizardArtifact artifact,
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != artifact.SizeBytes)
        {
            return false;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(
            Convert.ToHexStringLower(digest),
            artifact.Sha256,
            StringComparison.Ordinal);
    }

    private static async Task VerifyAndActivateAsync(
        BallsWizardArtifact artifact,
        string partialPath,
        string finalPath,
        IProgress<BallsWizardInstallProgress> progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(partialPath) || new FileInfo(partialPath).Length != artifact.SizeBytes)
        {
            throw new InvalidDataException("The downloaded artifact did not match its pinned size.");
        }

        progress.Report(
            new BallsWizardInstallProgress(
                artifact.Id,
                "verifying",
                artifact.SizeBytes,
                artifact.SizeBytes));
        if (!await IsVerifiedAsync(artifact, partialPath, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("The downloaded artifact did not match its pinned SHA-256.");
        }

        File.Move(partialPath, finalPath, overwrite: true);
        progress.Report(
            new BallsWizardInstallProgress(
                artifact.Id,
                "verified",
                artifact.SizeBytes,
                artifact.SizeBytes));
    }
}
