using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Balls.Canary;

internal enum CanaryPlatform
{
    Windows,
    Linux,
}

internal sealed record CanaryPackageRequest(
    string RepositoryRoot,
    string CliDirectory,
    string DaemonDirectory,
    string OutputDirectory,
    CanaryPlatform Platform,
    string Architecture,
    string Commit);

internal sealed record CanaryPackageResult(
    string ArtifactName,
    string ArchivePath,
    string ChecksumPath,
    string? InstallerPath);

internal static partial class CanaryPackageBuilder
{
    public static CanaryPackageResult Build(CanaryPackageRequest request)
    {
        Validate(request);

        var commit = request.Commit.ToLowerInvariant();
        var version = ReadVersion(request.RepositoryRoot);
        var platform = request.Platform.ToString().ToLowerInvariant();
        var artifactName =
            $"balls-{version}-canary-{platform}-{request.Architecture}-{commit[..12]}";
        Directory.CreateDirectory(request.OutputDirectory);

        var archivePath = Path.Combine(request.OutputDirectory, $"{artifactName}.zip");
        var checksumPath = $"{archivePath}.sha256";
        var installerSource = Path.Combine(
            request.RepositoryRoot,
            "eng",
            "canary",
            "Install-BallsCanary.ps1");
        var installerPath = request.Platform == CanaryPlatform.Windows
            ? Path.Combine(request.OutputDirectory, "Install-BallsCanary.ps1")
            : null;
        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"balls-canary-package-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            CopyDirectory(request.CliDirectory, Path.Combine(stagingDirectory, "balls"));
            CopyDirectory(request.DaemonDirectory, Path.Combine(stagingDirectory, "ballsd"));
            File.Copy(
                Path.Combine(request.RepositoryRoot, "LICENSE"),
                Path.Combine(stagingDirectory, "LICENSE"));

            if (request.Platform == CanaryPlatform.Windows)
            {
                File.Copy(installerSource, Path.Combine(stagingDirectory, "Install-BallsCanary.ps1"));
                File.Copy(installerSource, installerPath!, overwrite: true);
            }

            File.WriteAllText(
                Path.Combine(stagingDirectory, "README.md"),
                BuildReadme(request.Platform, artifactName),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            WriteManifest(
                Path.Combine(stagingDirectory, "canary.json"),
                version,
                commit,
                platform,
                request.Architecture,
                request.Platform);
            WriteInternalChecksums(stagingDirectory);

            File.Delete(archivePath);
            File.Delete(checksumPath);
            ZipFile.CreateFromDirectory(
                stagingDirectory,
                archivePath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);
            var archiveHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archivePath)));
            File.WriteAllText(
                checksumPath,
                $"{archiveHash}  {Path.GetFileName(archivePath)}{Environment.NewLine}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }

        return new CanaryPackageResult(
            artifactName,
            archivePath,
            checksumPath,
            installerPath);
    }

    private static void Validate(CanaryPackageRequest request)
    {
        if (!CommitPattern().IsMatch(request.Commit))
        {
            throw new ArgumentException("Commit must be a full 40-character SHA-1 identity.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Architecture) ||
            request.Architecture.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("Architecture must be an alphanumeric identifier.", nameof(request));
        }

        RequireDirectory(request.RepositoryRoot, nameof(request.RepositoryRoot));
        RequireDirectory(request.CliDirectory, nameof(request.CliDirectory));
        RequireDirectory(request.DaemonDirectory, nameof(request.DaemonDirectory));

        var cliName = request.Platform == CanaryPlatform.Windows ? "balls.exe" : "balls";
        var daemonName = request.Platform == CanaryPlatform.Windows ? "ballsd.exe" : "ballsd";
        RequireFile(Path.Combine(request.CliDirectory, cliName));
        RequireFile(Path.Combine(request.DaemonDirectory, daemonName));
        RequireFile(Path.Combine(request.RepositoryRoot, "Directory.Build.props"));
        RequireFile(Path.Combine(request.RepositoryRoot, "LICENSE"));
        if (request.Platform == CanaryPlatform.Windows)
        {
            RequireFile(Path.Combine(
                request.RepositoryRoot,
                "eng",
                "canary",
                "Install-BallsCanary.ps1"));
        }
    }

    private static string ReadVersion(string repositoryRoot)
    {
        var properties = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"));
        var prefix = properties.Descendants("VersionPrefix").Single().Value;
        var suffix = properties.Descendants("VersionSuffix").SingleOrDefault()?.Value;
        return string.IsNullOrWhiteSpace(suffix) ? prefix : $"{prefix}-{suffix}";
    }

    private static void WriteManifest(
        string path,
        string version,
        string commit,
        string platform,
        string architecture,
        CanaryPlatform canaryPlatform)
    {
        var manifest = new
        {
            product = "Balls",
            version,
            commit,
            platform,
            architecture,
            runtimeSupported = true,
            support = canaryPlatform == CanaryPlatform.Windows
                ? "Windows Canary for development use."
                : "Linux Canary for development use.",
        };
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) +
            Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string BuildReadme(CanaryPlatform platform, string artifactName) =>
        platform == CanaryPlatform.Windows
            ? $""""
              # Balls Windows Canary

              Development artifact `{artifactName}`. This is not a stable installer or release.

              From the downloaded workflow-artifact directory:

              ```powershell
              pwsh -File .\Install-BallsCanary.ps1 -PackagePath .\{artifactName}.zip
              ```
              """" + Environment.NewLine
            : $""""
              # Balls Linux Canary

              Development artifact `{artifactName}`. This is not a stable installer or release.

              Extract the archive, preserve executable bits, start `ballsd/ballsd`, and use
              `balls/balls status`. The daemon defaults to protected XDG state and a same-user
              Unix-domain socket.
              """" + Environment.NewLine;

    private static void WriteInternalChecksums(string stagingDirectory)
    {
        var lines = Directory.GetFiles(stagingDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Relative = Path.GetRelativePath(stagingDirectory, path).Replace('\\', '/'),
            })
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .Select(file =>
                $"{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.Path)))}  {file.Relative}");
        File.WriteAllText(
            Path.Combine(stagingDirectory, "SHA256SUMS"),
            string.Join(Environment.NewLine, lines) + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void RequireDirectory(string path, string parameterName)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Required {parameterName} directory not found: {path}");
        }
    }

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required Canary input not found.", path);
        }
    }

    [GeneratedRegex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();
}
