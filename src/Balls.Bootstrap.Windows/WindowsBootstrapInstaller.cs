using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Balls.Bootstrap.Windows;

internal sealed class WindowsBootstrapInstaller : IDisposable
{
    private const long MaximumPackageBytes = 2_147_483_648;
    private const int MaximumChecksumBytes = 1_024;
    private readonly VerifiedDownloader downloader = new();

    public async Task InstallAsync(BootstrapOptions options, CancellationToken cancellationToken)
    {
        RequireSafeInstallRoot(options);

        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"balls-install-{Guid.NewGuid():N}");
        var extractRoot = Path.Combine(temporaryRoot, "package");
        var recordPath = Path.Combine(options.InstallRoot, "installation.json");
        var recordTemporary = recordPath + ".new";
        var shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            "Balls.lnk");
        var previousRecord = ReadOptionalBytes(recordPath);
        var previousShortcut = ReadOptionalBytes(shortcutPath);
        byte[]? previousLauncher = null;
        string? launcherPath = null;
        string? installedVersionRoot = null;
        string? pidPath = null;
        Process? daemon = null;
        var createdVersion = false;
        var recordChanged = false;
        var shortcutChanged = false;
        var launcherChanged = false;
        var pidChanged = false;
        var committed = false;

        try
        {
            Directory.CreateDirectory(extractRoot);
            var candidate = options.IsManifestInstall
                ? await DownloadCandidateAsync(options.ManifestUri!, temporaryRoot, cancellationToken)
                    .ConfigureAwait(false)
                : ReadOfflineCandidate(options);

            RuntimeInspector.Require(candidate.Runtime);
            PackageVerifier.ValidateChecksumBinding(
                await File.ReadAllTextAsync(candidate.ChecksumPath, cancellationToken).ConfigureAwait(false),
                candidate.Archive);
            PackageVerifier.ReadAndValidateIdentity(candidate.PackagePath, candidate.Identity);
            PackageVerifier.ExtractAndValidate(candidate.PackagePath, extractRoot);

            var versionId = $"{candidate.Identity.Version}-{candidate.Identity.Commit[..12]}";
            var versionsRoot = Path.Combine(options.InstallRoot, "versions");
            installedVersionRoot = Path.Combine(versionsRoot, versionId);
            var stateRoot = Path.Combine(options.InstallRoot, "state");
            Directory.CreateDirectory(versionsRoot);
            Directory.CreateDirectory(stateRoot);

            if (Directory.Exists(installedVersionRoot))
            {
                if (PackageVerifier.ReadInstalledIdentity(installedVersionRoot) != candidate.Identity)
                {
                    throw new InvalidDataException(
                        "The installed Windows package identity does not match the selected Balls manifest.");
                }
                PackageVerifier.ValidateInternalChecksums(installedVersionRoot);
            }
            else
            {
                Directory.Move(extractRoot, installedVersionRoot);
                createdVersion = true;
            }

            pidPath = Path.Combine(options.InstallRoot, "ballsd.pid");
            RefuseRunningDaemon(pidPath);

            var daemonPath = Path.Combine(installedVersionRoot, "ballsd", "ballsd.exe");
            var cliPath = Path.Combine(installedVersionRoot, "balls", "balls.exe");
            launcherPath = Path.Combine(options.InstallRoot, "launchers", $"{versionId}.cmd");
            previousLauncher = ReadOptionalBytes(launcherPath);
            WriteLauncher(
                launcherPath,
                versionId,
                options.PipeName,
                options.NodeName,
                options.AdvertisedPrivateAddress);
            launcherChanged = true;

            daemon = StartDaemon(daemonPath, installedVersionRoot, stateRoot, options);
            await File.WriteAllTextAsync(pidPath, daemon.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
                .ConfigureAwait(false);
            pidChanged = true;
            var status = await WaitUntilReadyAsync(cliPath, options.PipeName, daemon, cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine(status);

            if (options.OpenUi)
            {
                var ui = await RunCliAsync(cliPath, ["--pipe-name", options.PipeName, "ui"], cancellationToken)
                    .ConfigureAwait(false);
                if (ui.ExitCode != 0 || !string.Equals(
                        ui.StandardOutput.Trim(),
                        "Opened the local Balls workspace.",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Balls started, but its workspace could not open.");
                }
            }

            if (options.CreateShortcut)
            {
                if (!OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException("Windows shortcuts require Windows.");
                }
                shortcutChanged = true;
                _ = WindowsShortcut.Create(launcherPath, installedVersionRoot);
            }

            var record = new
            {
                schemaVersion = 1,
                product = "Balls",
                channel = candidate.Channel,
                manifestUri = options.ManifestUri?.AbsoluteUri,
                installedAt = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                release = new { tag = candidate.Tag, commit = candidate.Identity.Commit },
                package = new
                {
                    name = candidate.Archive.Name,
                    sha256 = candidate.Archive.Sha256,
                    version = candidate.Identity.Version,
                    platform = "windows",
                    architecture = "x64",
                },
            };
            Directory.CreateDirectory(options.InstallRoot);
            await File.WriteAllTextAsync(
                    recordTemporary,
                    JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine,
                    new UTF8Encoding(false),
                    cancellationToken)
                .ConfigureAwait(false);
            recordChanged = true;
            File.Move(recordTemporary, recordPath, overwrite: true);
            committed = true;

            Console.WriteLine(
                $"Installed Balls {candidate.Identity.Version} from {candidate.Channel} release " +
                $"{candidate.Tag} ({candidate.Identity.Commit[..12]}).");
            if (options.CreateShortcut)
            {
                Console.WriteLine($"Shortcut: {shortcutPath}");
            }
            Console.WriteLine("This prerelease is unsigned. No Windows policy was bypassed.");
        }
        catch
        {
            if (daemon is { HasExited: false })
            {
                daemon.Kill(entireProcessTree: true);
                await daemon.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            if (pidChanged && pidPath is not null && File.Exists(pidPath))
            {
                File.Delete(pidPath);
            }
            if (!committed)
            {
                RestoreFile(recordPath, previousRecord, recordChanged);
                RestoreFile(shortcutPath, previousShortcut, shortcutChanged);
                if (launcherPath is not null)
                {
                    RestoreFile(launcherPath, previousLauncher, launcherChanged);
                }
                if (createdVersion && installedVersionRoot is not null && Directory.Exists(installedVersionRoot))
                {
                    Directory.Delete(installedVersionRoot, recursive: true);
                }
            }
            throw;
        }
        finally
        {
            daemon?.Dispose();
            if (Directory.Exists(temporaryRoot) && IsInside(temporaryRoot, Path.GetTempPath()))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    public void Dispose() => downloader.Dispose();

    private async Task<InstallCandidate> DownloadCandidateAsync(
        Uri manifestUri,
        string temporaryRoot,
        CancellationToken cancellationToken)
    {
        ReleaseManifestReader.ValidateOfficialManifestUri(manifestUri);
        var manifestBytes = await downloader.DownloadBytesAsync(manifestUri, 262_144, cancellationToken)
            .ConfigureAwait(false);
        var release = ReleaseManifestReader.Read(manifestBytes);
        var packagePath = Path.Combine(temporaryRoot, release.Archive.Name);
        var checksumPath = Path.Combine(temporaryRoot, release.Checksum.Name);
        await downloader.DownloadVerifiedAssetAsync(
                release.Archive,
                packagePath,
                MaximumPackageBytes,
                cancellationToken)
            .ConfigureAwait(false);
        await downloader.DownloadVerifiedAssetAsync(
                release.Checksum,
                checksumPath,
                MaximumChecksumBytes,
                cancellationToken)
            .ConfigureAwait(false);
        return new InstallCandidate(
            release.Channel,
            release.Tag,
            release.Identity,
            release.Runtime,
            release.Archive,
            packagePath,
            checksumPath);
    }

    private static InstallCandidate ReadOfflineCandidate(BootstrapOptions options)
    {
        var packagePath = Path.GetFullPath(options.PackagePath!);
        var checksumPath = Path.GetFullPath(options.ChecksumPath!);
        if (!File.Exists(packagePath) || !File.Exists(checksumPath))
        {
            throw new FileNotFoundException("The Windows Canary package or checksum is missing.");
        }
        var identity = PackageVerifier.ReadIdentity(packagePath);
        var archive = new ReleaseAsset(
            Path.GetFileName(packagePath),
            new Uri("https://github.com/scwlkr/balls/"),
            PackageVerifier.HashFile(packagePath));
        return new InstallCandidate(
            "canary",
            $"canary-{identity.Commit[..12]}",
            identity,
            new RuntimeContract("self-contained", "x64", []),
            archive,
            packagePath,
            checksumPath);
    }

    private static void RequireSafeInstallRoot(BootstrapOptions options)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (options.IsManifestInstall)
        {
            if (!IsInside(options.InstallRoot, localAppData))
            {
                throw new InvalidOperationException("Balls installs only inside the current user profile.");
            }
            return;
        }
        if (!IsInside(options.InstallRoot, localAppData) && !IsInside(options.InstallRoot, Path.GetTempPath()))
        {
            throw new InvalidOperationException("The Canary smoke install root must be temporary or current-user local data.");
        }
    }

    private static bool IsInside(string candidate, string parent)
    {
        var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(candidate);
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void RefuseRunningDaemon(string pidPath)
    {
        if (!File.Exists(pidPath))
        {
            return;
        }
        if (!int.TryParse(File.ReadAllText(pidPath), out var pid))
        {
            File.Delete(pidPath);
            return;
        }
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                throw new InvalidOperationException($"Balls is already running as PID {pid}. Close Balls before updating.");
            }
        }
        catch (ArgumentException)
        {
            // The recorded process no longer exists.
        }
        File.Delete(pidPath);
    }

    private static Process StartDaemon(
        string daemonPath,
        string workingDirectory,
        string stateRoot,
        BootstrapOptions options)
    {
        var startInfo = new ProcessStartInfo(daemonPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.Combine(workingDirectory, "ballsd"),
        };
        foreach (var argument in BuildDaemonArguments(stateRoot, options))
        {
            startInfo.ArgumentList.Add(argument);
        }
        try
        {
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows did not create the Balls process.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            throw new InvalidOperationException(
                $"BLOCKED: Windows did not allow Balls to start. No application policy was changed. {exception.Message}",
                exception);
        }
    }

    internal static IReadOnlyList<string> BuildDaemonArguments(
        string stateRoot,
        BootstrapOptions options)
    {
        var arguments = new List<string>
        {
            "--data-directory", stateRoot,
            "--pipe-name", options.PipeName,
            "--node-name", options.NodeName,
            "--automatic-private-listeners",
        };
        if (options.AdvertisedPrivateAddress is not null)
        {
            arguments.Add("--advertised-private-address");
            arguments.Add(options.AdvertisedPrivateAddress);
        }
        return arguments;
    }

    private static async Task<string> WaitUntilReadyAsync(
        string cliPath,
        string pipeName,
        Process daemon,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        var lastError = string.Empty;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (daemon.HasExited)
            {
                throw new InvalidOperationException($"Balls exited during startup with code {daemon.ExitCode}.");
            }
            var result = await RunCliAsync(
                    cliPath,
                    ["--pipe-name", pipeName, "status"],
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return result.StandardOutput.TrimEnd();
            }
            lastError = result.StandardError.Trim();
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException($"Balls did not become ready. Last startup check: {lastError}");
    }

    private static async Task<ProcessResult> RunCliAsync(
        string cliPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(cliPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows did not create the Balls command process.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    private static void WriteLauncher(
        string path,
        string versionId,
        string pipeName,
        string nodeName,
        string? advertisedPrivateAddress)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = RenderLauncher(versionId, pipeName, nodeName, advertisedPrivateAddress);
        var temporary = path + ".new";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    internal static string RenderLauncher(
        string versionId,
        string pipeName,
        string nodeName,
        string? advertisedPrivateAddress)
    {
        if (!SafeValue(versionId)
            || !SafeValue(pipeName)
            || nodeName.IndexOfAny(['"', '\r', '\n']) >= 0
            || (advertisedPrivateAddress is not null && !SafeValue(advertisedPrivateAddress)))
        {
            throw new InvalidDataException("The installed package produced an unsafe launcher identity.");
        }
        var advertisedArgument = advertisedPrivateAddress is null
            ? string.Empty
            : $" --advertised-private-address \"{advertisedPrivateAddress}\"";
        return LauncherTemplate
            .Replace("{VERSION_ID}", versionId, StringComparison.Ordinal)
            .Replace("{PIPE_NAME}", pipeName, StringComparison.Ordinal)
            .Replace("{NODE_NAME}", nodeName, StringComparison.Ordinal)
            .Replace("{ADVERTISED_PRIVATE_ARGUMENT}", advertisedArgument, StringComparison.Ordinal);
    }

    private static bool SafeValue(string value) =>
        value.Length is > 0 and <= 160 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static byte[]? ReadOptionalBytes(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private static void RestoreFile(string path, byte[]? previous, bool changed)
    {
        if (!changed)
        {
            return;
        }
        if (previous is null)
        {
            File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, previous);
    }

    private sealed record InstallCandidate(
        string Channel,
        string Tag,
        PackageIdentity Identity,
        RuntimeContract Runtime,
        ReleaseAsset Archive,
        string PackagePath,
        string ChecksumPath);

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private const string LauncherTemplate = """
@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "BALLS_PACKAGE=%~dp0..\versions\{VERSION_ID}"
set "BALLS_HOME=%~dp0.."
set "BALLS_PIPE={PIPE_NAME}"
set "BALLS_NODE={NODE_NAME}"
set "BALLS_CLI=%BALLS_PACKAGE%\balls\balls.exe"
set "BALLS_DAEMON=%BALLS_PACKAGE%\ballsd\ballsd.exe"
set "BALLS_DAEMON_DIRECTORY=%BALLS_PACKAGE%\ballsd"
set "BALLS_STATE=%BALLS_HOME%\state"
set "BALLS_LOGS=%BALLS_HOME%\logs"
set "BALLS_STDOUT=%BALLS_LOGS%\ballsd.stdout.log"
set "BALLS_STDERR=%BALLS_LOGS%\ballsd.stderr.log"
set "BALLS_PID=%BALLS_HOME%\ballsd.pid"
set "BALLS_DAEMON_ARGUMENTS=--data-directory "%BALLS_STATE%" --pipe-name "%BALLS_PIPE%" --node-name "%BALLS_NODE%" --automatic-private-listeners{ADVERTISED_PRIVATE_ARGUMENT}"
if not exist "%BALLS_CLI%" goto missing_files
if not exist "%BALLS_DAEMON%" goto missing_files
"%BALLS_CLI%" --pipe-name "%BALLS_PIPE%" status >nul 2>&1
if not errorlevel 1 goto open_workspace
if not exist "%BALLS_STATE%" mkdir "%BALLS_STATE%"
if not exist "%BALLS_LOGS%" mkdir "%BALLS_LOGS%"
powershell.exe -NoLogo -NoProfile -NonInteractive -Command ^
  "$process = $null; try { $process = Start-Process -FilePath $env:BALLS_DAEMON -ArgumentList $env:BALLS_DAEMON_ARGUMENTS -WorkingDirectory $env:BALLS_DAEMON_DIRECTORY -WindowStyle Hidden -RedirectStandardOutput $env:BALLS_STDOUT -RedirectStandardError $env:BALLS_STDERR -PassThru -ErrorAction Stop; $process.Id | Set-Content -LiteralPath $env:BALLS_PID -Encoding ascii -ErrorAction Stop; exit 0 } catch { if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }; $_ | Out-String | Set-Content -LiteralPath $env:BALLS_STDERR; exit 1 }"
if errorlevel 1 goto startup_failed
set /a BALLS_ATTEMPTS=30
:wait_for_node
"%BALLS_CLI%" --pipe-name "%BALLS_PIPE%" status >nul 2>&1
if not errorlevel 1 goto open_workspace
set /a BALLS_ATTEMPTS-=1
if %BALLS_ATTEMPTS% leq 0 goto startup_failed
ping -n 2 127.0.0.1 >nul
goto wait_for_node
:open_workspace
"%BALLS_CLI%" --pipe-name "%BALLS_PIPE%" ui
if errorlevel 1 goto workspace_failed
exit /b 0
:missing_files
echo Balls is incomplete. Run the install command again.
pause
exit /b 1
:startup_failed
echo Balls could not start. Windows application policy was not changed.
echo Startup log: %BALLS_STDERR%
if exist "%BALLS_STDERR%" type "%BALLS_STDERR%"
pause
exit /b 1
:workspace_failed
echo Balls is running, but its workspace could not open. Try again.
pause
exit /b 1
""";
}
