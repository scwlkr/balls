using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Balls.Canary;

namespace Balls.Canary.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class CanaryPackageTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [TestMethod]
    public void Package_command_maps_explicit_build_inputs()
    {
        var request = CanaryCommandParser.Parse(
            [
                "package",
                "--repository-root", "C:/repo",
                "--cli-directory", "C:/cli",
                "--daemon-directory", "C:/daemon",
                "--output-directory", "C:/output",
                "--platform", "windows",
                "--architecture", "x64",
                "--commit", Commit,
            ]);

        Assert.AreEqual("C:/repo", request.RepositoryRoot);
        Assert.AreEqual("C:/cli", request.CliDirectory);
        Assert.AreEqual("C:/daemon", request.DaemonDirectory);
        Assert.AreEqual("C:/output", request.OutputDirectory);
        Assert.AreEqual(CanaryPlatform.Windows, request.Platform);
        Assert.AreEqual("x64", request.Architecture);
        Assert.AreEqual(Commit, request.Commit);
    }

    [TestMethod]
    public void Package_command_rejects_missing_or_unknown_inputs()
    {
        Assert.ThrowsExactly<CanaryUsageException>(() =>
            CanaryCommandParser.Parse(["package", "--platform", "windows"]));
        Assert.ThrowsExactly<CanaryUsageException>(() =>
            CanaryCommandParser.Parse(
                [
                    "package",
                    "--repository-root", "C:/repo",
                    "--cli-directory", "C:/cli",
                    "--daemon-directory", "C:/daemon",
                    "--output-directory", "C:/output",
                    "--platform", "macos",
                    "--architecture", "x64",
                    "--commit", Commit,
                ]));
    }

    [TestMethod]
    public void Windows_package_contains_runnable_outputs_identity_and_checksums()
    {
        using var fixture = new PackageFixture("balls.exe", "ballsd.exe");

        var result = CanaryPackageBuilder.Build(fixture.Request(CanaryPlatform.Windows));

        Assert.AreEqual(
            "balls-0.1.0-alpha.1-canary-windows-x64-0123456789ab",
            result.ArtifactName);
        Assert.IsTrue(File.Exists(result.ArchivePath));
        Assert.IsTrue(File.Exists(result.ChecksumPath));
        Assert.IsNotNull(result.InstallerPath);
        Assert.IsTrue(File.Exists(result.InstallerPath));

        using var archive = ZipFile.OpenRead(result.ArchivePath);
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "balls/balls.exe",
                "ballsd/ballsd.exe",
                "canary.json",
                "SHA256SUMS",
                "LICENSE",
                "README.md",
                "Install-BallsCanary.ps1",
            },
            archive.Entries.Select(entry => entry.FullName).ToArray());

        using var manifest = ReadJson(archive, "canary.json");
        Assert.AreEqual("0.1.0-alpha.1", manifest.RootElement.GetProperty("version").GetString());
        Assert.AreEqual(Commit, manifest.RootElement.GetProperty("commit").GetString());
        Assert.AreEqual("windows", manifest.RootElement.GetProperty("platform").GetString());
        Assert.AreEqual("x64", manifest.RootElement.GetProperty("architecture").GetString());
        Assert.IsTrue(manifest.RootElement.GetProperty("runtimeSupported").GetBoolean());

        var expectedArchiveHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.ArchivePath)));
        Assert.AreEqual(
            $"{expectedArchiveHash}  {Path.GetFileName(result.ArchivePath)}{Environment.NewLine}",
            File.ReadAllText(result.ChecksumPath));
        AssertInternalChecksums(archive);
    }

    [TestMethod]
    public void Linux_package_is_explicitly_runtime_unsupported()
    {
        using var fixture = new PackageFixture("balls", "ballsd");

        var result = CanaryPackageBuilder.Build(fixture.Request(CanaryPlatform.Linux));

        Assert.AreEqual(
            "balls-0.1.0-alpha.1-canary-linux-x64-0123456789ab",
            result.ArtifactName);
        Assert.IsNull(result.InstallerPath);
        using var archive = ZipFile.OpenRead(result.ArchivePath);
        using var manifest = ReadJson(archive, "canary.json");
        Assert.IsFalse(manifest.RootElement.GetProperty("runtimeSupported").GetBoolean());
        var readme = ReadText(archive, "README.md");
        StringAssert.Contains(readme, "Runtime unsupported until 0.2.0-alpha.1");
        AssertInternalChecksums(archive);
    }

    [TestMethod]
    public void Package_rejects_a_non_commit_identity()
    {
        using var fixture = new PackageFixture("balls.exe", "ballsd.exe");
        var request = fixture.Request(CanaryPlatform.Windows) with { Commit = "main" };

        Assert.ThrowsExactly<ArgumentException>(() => CanaryPackageBuilder.Build(request));
    }

    private static JsonDocument ReadJson(ZipArchive archive, string path) =>
        JsonDocument.Parse(ReadText(archive, path));

    private static string ReadText(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new AssertFailedException($"Missing {path}");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static void AssertInternalChecksums(ZipArchive archive)
    {
        var expected = ReadText(archive, "SHA256SUMS")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Select(line => line.Split("  ", 2, StringSplitOptions.None))
            .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);

        foreach (var entry in archive.Entries.Where(entry => entry.Name.Length > 0 && entry.FullName != "SHA256SUMS"))
        {
            using var stream = entry.Open();
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            Assert.AreEqual(expected[entry.FullName], actual, entry.FullName);
        }
    }

    private sealed class PackageFixture : IDisposable
    {
        private readonly string root;

        public PackageFixture(string cliFileName, string daemonFileName)
        {
            root = Path.Combine(Path.GetTempPath(), $"balls-canary-tests-{Guid.NewGuid():N}");
            RepositoryRoot = Path.Combine(root, "repo");
            CliDirectory = Path.Combine(root, "cli");
            DaemonDirectory = Path.Combine(root, "daemon");
            OutputDirectory = Path.Combine(root, "output");
            Directory.CreateDirectory(RepositoryRoot);
            Directory.CreateDirectory(CliDirectory);
            Directory.CreateDirectory(DaemonDirectory);
            File.WriteAllText(
                Path.Combine(RepositoryRoot, "Directory.Build.props"),
                "<Project><PropertyGroup><VersionPrefix>0.1.0</VersionPrefix>" +
                "<VersionSuffix>alpha.1</VersionSuffix></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(RepositoryRoot, "LICENSE"), "Apache 2.0 test fixture");
            Directory.CreateDirectory(Path.Combine(RepositoryRoot, "eng", "canary"));
            File.WriteAllText(
                Path.Combine(RepositoryRoot, "eng", "canary", "Install-BallsCanary.ps1"),
                "# test installer");
            File.WriteAllText(Path.Combine(CliDirectory, cliFileName), "cli");
            File.WriteAllText(Path.Combine(DaemonDirectory, daemonFileName), "daemon");
        }

        public string RepositoryRoot { get; }

        public string CliDirectory { get; }

        public string DaemonDirectory { get; }

        public string OutputDirectory { get; }

        public CanaryPackageRequest Request(CanaryPlatform platform) =>
            new(
                RepositoryRoot,
                CliDirectory,
                DaemonDirectory,
                OutputDirectory,
                platform,
                "x64",
                Commit);

        public void Dispose()
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
