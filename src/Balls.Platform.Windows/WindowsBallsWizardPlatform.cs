using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Balls.Platform;

namespace Balls.Platform.Windows;

public sealed class WindowsBallsWizardPlatform : IBallsWizardPlatform
{
    private const int MaxResponseBytes = 256 * 1024;
    private readonly HttpClient downloadClient;
    private readonly HttpClient runtimeClient;
    private readonly SemaphoreSlim runtimeGate = new(1, 1);
    private Process? runtimeProcess;
    private Task? runtimeStandardOutput;
    private Task? runtimeStandardError;
    private Uri? runtimeBaseUri;
    private string? runtimeApiKey;
    private int disposed;

    public WindowsBallsWizardPlatform()
        : this(CreateDownloadClient(), CreateRuntimeClient())
    {
    }

    internal WindowsBallsWizardPlatform(HttpClient downloadClient, HttpClient runtimeClient)
    {
        this.downloadClient = downloadClient;
        this.runtimeClient = runtimeClient;
    }

    public Task<BallsWizardInspection> InspectAsync(
        string wizardDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var paths = WindowsBallsWizardPaths.FromRoot(wizardDirectory);

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                != System.Runtime.InteropServices.Architecture.X64
            || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                != System.Runtime.InteropServices.Architecture.X64)
        {
            return Task.FromResult(
                UnsupportedInspection(
                    "windows_11_x64_required",
                    "Balls Wizard v0 needs Windows 11 x64.",
                    wizardDirectory));
        }

        var context = WindowsBallsWizardSystemInventory.Inspect(wizardDirectory);
        var installation = InspectInstallation(paths);
        var downloaded = CountDownloadedBytes(paths);
        if (context.TotalMemoryBytes < WindowsBallsWizardArtifacts.MinimumMemoryBytes)
        {
            return Task.FromResult(
                CreateInspection(
                    BallsWizardSupportStatus.Blocked,
                    installation,
                    "wizard_memory_required",
                    "This Node needs at least 8 GiB of memory for Balls Wizard v0.",
                    downloaded,
                    context));
        }

        if (context.FreeStorageBytes < WindowsBallsWizardArtifacts.RequiredStorageBytes
            && installation != BallsWizardInstallationStatus.Installed)
        {
            return Task.FromResult(
                CreateInspection(
                    BallsWizardSupportStatus.Blocked,
                    installation,
                    "wizard_storage_required",
                    "This Node needs at least 5 GiB free in the Balls data location.",
                    downloaded,
                    context));
        }

        var message = installation switch
        {
            BallsWizardInstallationStatus.Installed =>
                "The local Wizard is installed and ready to wake up.",
            BallsWizardInstallationStatus.Partial =>
                "A verified Wizard download can resume where it stopped.",
            _ => "This Node is ready to download Balls Wizard.",
        };
        return Task.FromResult(
            CreateInspection(
                BallsWizardSupportStatus.Supported,
                installation,
                "wizard_supported",
                message,
                downloaded,
                context));
    }

    public async Task InstallAsync(
        string wizardDirectory,
        IProgress<BallsWizardInstallProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ThrowIfDisposed();
        var inspection = await InspectAsync(wizardDirectory, cancellationToken).ConfigureAwait(false);
        if (inspection.Support != BallsWizardSupportStatus.Supported)
        {
            throw new InvalidOperationException(inspection.Message);
        }

        if (inspection.Installation == BallsWizardInstallationStatus.Installed)
        {
            return;
        }

        var paths = WindowsBallsWizardPaths.FromRoot(wizardDirectory);
        Directory.CreateDirectory(paths.Root);
        Directory.CreateDirectory(paths.Downloads);
        var downloader = new WizardArtifactDownloader(downloadClient);
        await downloader.DownloadAsync(
            WindowsBallsWizardArtifacts.Runtime,
            paths.RuntimeArchive,
            progress,
            cancellationToken).ConfigureAwait(false);
        await downloader.DownloadAsync(
            WindowsBallsWizardArtifacts.Model,
            paths.ModelFile,
            progress,
            cancellationToken).ConfigureAwait(false);

        progress.Report(
            new BallsWizardInstallProgress(
                WindowsBallsWizardArtifacts.Runtime.Id,
                "extracting",
                WindowsBallsWizardArtifacts.Runtime.SizeBytes,
                WindowsBallsWizardArtifacts.Runtime.SizeBytes));
        ExtractRuntime(paths, cancellationToken);
        var installation = new InstalledWizardRecord(
            WindowsBallsWizardArtifacts.WizardVersion,
            WindowsBallsWizardArtifacts.Runtime.Version,
            WindowsBallsWizardArtifacts.Runtime.Sha256,
            WindowsBallsWizardArtifacts.Model.Version,
            WindowsBallsWizardArtifacts.Model.Sha256,
            DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(
            paths.InstallationRecord,
            JsonSerializer.Serialize(installation, InstalledWizardJsonContext.Default.InstalledWizardRecord),
            cancellationToken).ConfigureAwait(false);
        File.Delete(paths.RuntimeArchive);
        progress.Report(
            new BallsWizardInstallProgress(
                "wizard",
                "installed",
                WindowsBallsWizardArtifacts.All.Sum(artifact => artifact.SizeBytes),
                WindowsBallsWizardArtifacts.All.Sum(artifact => artifact.SizeBytes)));
    }

    public async Task<string> CompleteAsync(
        string wizardDirectory,
        string systemPrompt,
        IReadOnlyList<BallsWizardChatMessage> messages,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentNullException.ThrowIfNull(messages);
        ThrowIfDisposed();
        var paths = WindowsBallsWizardPaths.FromRoot(wizardDirectory);
        if (InspectInstallation(paths) != BallsWizardInstallationStatus.Installed)
        {
            throw new InvalidOperationException("Balls Wizard is not installed.");
        }

        await EnsureRuntimeAsync(paths, cancellationToken).ConfigureAwait(false);
        var requestMessages = new List<object>(messages.Count + 1)
        {
            new { role = "system", content = systemPrompt },
        };
        requestMessages.AddRange(messages.Select(message => new
        {
            role = message.Role,
            content = message.Content,
        }));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(runtimeBaseUri!, "/v1/chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", runtimeApiKey);
        request.Content = JsonContent.Create(new
        {
            model = "balls-wizard",
            messages = requestMessages,
            temperature = 0.25,
            max_tokens = 600,
            stream = false,
        });
        using var response = await runtimeClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var bounded = await ReadBoundedAsync(
            responseStream,
            MaxResponseBytes,
            cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(
            bounded,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidDataException("The local Wizard returned an empty answer.");
        }

        return content.Trim();
    }

    public async Task RemoveAsync(string wizardDirectory, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var paths = WindowsBallsWizardPaths.FromRoot(wizardDirectory);
        await StopRuntimeAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(paths.Root))
        {
            Directory.Delete(paths.Root, recursive: true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await StopRuntimeAsync().ConfigureAwait(false);
        runtimeGate.Dispose();
        downloadClient.Dispose();
        runtimeClient.Dispose();
    }

    private static BallsWizardInstallationStatus InspectInstallation(WindowsBallsWizardPaths paths)
    {
        if (File.Exists(paths.InstallationRecord)
            && File.Exists(paths.RuntimeExecutable)
            && File.Exists(paths.ModelFile)
            && new FileInfo(paths.ModelFile).Length == WindowsBallsWizardArtifacts.Model.SizeBytes)
        {
            try
            {
                var record = JsonSerializer.Deserialize(
                    File.ReadAllText(paths.InstallationRecord),
                    InstalledWizardJsonContext.Default.InstalledWizardRecord);
                if (record is not null
                    && record.WizardVersion == WindowsBallsWizardArtifacts.WizardVersion
                    && record.RuntimeSha256 == WindowsBallsWizardArtifacts.Runtime.Sha256
                    && record.ModelSha256 == WindowsBallsWizardArtifacts.Model.Sha256)
                {
                    return BallsWizardInstallationStatus.Installed;
                }
            }
            catch (JsonException)
            {
            }
        }

        return CountDownloadedBytes(paths) > 0
            ? BallsWizardInstallationStatus.Partial
            : BallsWizardInstallationStatus.Absent;
    }

    private static long CountDownloadedBytes(WindowsBallsWizardPaths paths)
    {
        return CountArtifactBytes(paths.RuntimeArchive, WindowsBallsWizardArtifacts.Runtime.SizeBytes)
            + CountArtifactBytes(paths.ModelFile, WindowsBallsWizardArtifacts.Model.SizeBytes);
    }

    private static long CountArtifactBytes(string finalPath, long pinnedSize)
    {
        var path = File.Exists(finalPath) ? finalPath : finalPath + ".partial";
        return File.Exists(path) ? Math.Min(new FileInfo(path).Length, pinnedSize) : 0;
    }

    private static BallsWizardInspection CreateInspection(
        BallsWizardSupportStatus support,
        BallsWizardInstallationStatus installation,
        string code,
        string message,
        long downloaded,
        BallsWizardSystemContext context)
    {
        return new BallsWizardInspection(
            support,
            installation,
            code,
            message,
            WindowsBallsWizardArtifacts.WizardVersion,
            downloaded,
            WindowsBallsWizardArtifacts.RequiredStorageBytes,
            context,
            WindowsBallsWizardArtifacts.All);
    }

    private static BallsWizardInspection UnsupportedInspection(
        string code,
        string message,
        string wizardDirectory)
    {
        var freeStorage = 0L;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(wizardDirectory));
            if (root is not null)
            {
                freeStorage = new DriveInfo(root).AvailableFreeSpace;
            }
        }
        catch (IOException)
        {
        }

        return CreateInspection(
            BallsWizardSupportStatus.Unsupported,
            BallsWizardInstallationStatus.Absent,
            code,
            message,
            0,
            new BallsWizardSystemContext(
                System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                "Unavailable",
                [],
                0,
                0,
                freeStorage));
    }

    private static void ExtractRuntime(
        WindowsBallsWizardPaths paths,
        CancellationToken cancellationToken)
    {
        var staging = paths.RuntimeDirectory + ".staging";
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }
        Directory.CreateDirectory(staging);

        try
        {
            using var archive = ZipFile.OpenRead(paths.RuntimeArchive);
            var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The Wizard runtime archive escaped its staging directory.");
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: false);
            }

            if (!File.Exists(Path.Combine(staging, "llama-server.exe")))
            {
                throw new InvalidDataException("The pinned Wizard runtime did not contain llama-server.exe.");
            }

            if (Directory.Exists(paths.RuntimeDirectory))
            {
                Directory.Delete(paths.RuntimeDirectory, recursive: true);
            }
            Directory.Move(staging, paths.RuntimeDirectory);
        }
        catch
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
            throw;
        }
    }

    private async Task EnsureRuntimeAsync(
        WindowsBallsWizardPaths paths,
        CancellationToken cancellationToken)
    {
        await runtimeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (runtimeProcess is { HasExited: false } && runtimeBaseUri is not null)
            {
                return;
            }

            await StopRuntimeCoreAsync().ConfigureAwait(false);
            var port = ReserveLoopbackPort();
            runtimeApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            runtimeBaseUri = new Uri($"http://127.0.0.1:{port}");
            var startInfo = new ProcessStartInfo(paths.RuntimeExecutable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = paths.RuntimeDirectory,
            };
            AddRuntimeArguments(startInfo, paths.ModelFile, port, runtimeApiKey);
            runtimeProcess = Process.Start(startInfo)
                ?? throw new IOException("Windows did not start the local Wizard runtime.");
            runtimeStandardOutput = DrainAsync(runtimeProcess.StandardOutput);
            runtimeStandardError = DrainAsync(runtimeProcess.StandardError);

            using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupTimeout.CancelAfter(TimeSpan.FromMinutes(2));
            while (true)
            {
                startupTimeout.Token.ThrowIfCancellationRequested();
                if (runtimeProcess.HasExited)
                {
                    throw new IOException("The local Wizard runtime stopped while loading the model.");
                }

                try
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        new Uri(runtimeBaseUri, "/health"));
                    using var response = await runtimeClient.SendAsync(
                        request,
                        startupTimeout.Token).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                }

                await Task.Delay(TimeSpan.FromMilliseconds(300), startupTimeout.Token)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            await StopRuntimeCoreAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            runtimeGate.Release();
        }
    }

    private async Task StopRuntimeAsync()
    {
        await runtimeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopRuntimeCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            runtimeGate.Release();
        }
    }

    private async Task StopRuntimeCoreAsync()
    {
        if (runtimeProcess is not null)
        {
            if (!runtimeProcess.HasExited)
            {
                runtimeProcess.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await runtimeProcess.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
            runtimeProcess.Dispose();
        }

        if (runtimeStandardOutput is not null)
        {
            await IgnoreDrainFailureAsync(runtimeStandardOutput).ConfigureAwait(false);
        }
        if (runtimeStandardError is not null)
        {
            await IgnoreDrainFailureAsync(runtimeStandardError).ConfigureAwait(false);
        }

        runtimeProcess = null;
        runtimeStandardOutput = null;
        runtimeStandardError = null;
        runtimeBaseUri = null;
        runtimeApiKey = null;
    }

    private static void AddRuntimeArguments(
        ProcessStartInfo startInfo,
        string modelPath,
        int port,
        string apiKey)
    {
        var arguments = new[]
        {
            "--model", modelPath,
            "--host", "127.0.0.1",
            "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--ctx-size", "4096",
            "--threads", Math.Clamp(Environment.ProcessorCount - 1, 1, 8)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--api-key", apiKey,
            "--no-webui",
            "--log-disable",
            "--reasoning", "off",
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) > 0)
        {
        }
    }

    private static async Task IgnoreDrainFailureAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task<MemoryStream> ReadBoundedAsync(
        Stream source,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var result = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                result.Position = 0;
                return result;
            }
            if (result.Length + count > maximumBytes)
            {
                result.Dispose();
                throw new InvalidDataException("The local Wizard returned an oversized response.");
            }
            await result.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
    }

    private static HttpClient CreateDownloadClient()
    {
        var client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Balls-Wizard/0");
        return client;
    }

    private static HttpClient CreateRuntimeClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3),
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
    }
}

internal sealed record InstalledWizardRecord(
    string WizardVersion,
    string RuntimeVersion,
    string RuntimeSha256,
    string ModelVersion,
    string ModelSha256,
    DateTimeOffset InstalledAtUtc);

[System.Text.Json.Serialization.JsonSerializable(typeof(InstalledWizardRecord))]
internal sealed partial class InstalledWizardJsonContext : JsonSerializerContext;
