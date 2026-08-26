using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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
    public void Development_manifest_command_maps_exact_release_inputs()
    {
        var request = DevelopmentManifestCommandParser.Parse(
            [
                "development-manifest",
                "--public-root", "C:/site/public",
                "--package-path", "C:/release/balls.zip",
                "--checksum-path", "C:/release/balls.zip.sha256",
                "--installer-path", "C:/release/Install-BallsCanary.ps1",
                "--tag", "development-20260826T120000Z-0123456789ab",
                "--commit", Commit,
                "--published-at", "2026-08-26T12:00:00Z",
            ]);

        Assert.AreEqual("C:/site/public", request.PublicRoot);
        Assert.AreEqual("C:/release/balls.zip", request.PackagePath);
        Assert.AreEqual("development-20260826T120000Z-0123456789ab", request.Tag);
        Assert.AreEqual(Commit, request.Commit);
        Assert.AreEqual("2026-08-26T12:00:00Z", request.PublishedAt);
    }

    [TestMethod]
    public void Development_manifest_generation_binds_package_identity_and_keeps_ten_rows()
    {
        using var fixture = new DevelopmentManifestFixture(selfContained: true);
        DevelopmentManifestResult? result = null;

        for (var index = 0; index < 12; index++)
        {
            var tag = $"development-20260826T{12 + index:D2}0000Z-0123456789ab";
            result = DevelopmentManifestBuilder.Build(fixture.Request(tag, 12 + index));
        }

        Assert.IsNotNull(result);
        Assert.IsNotNull(result.PreviousTag);
        Assert.IsNotNull(result.PreviousSha256);
        Assert.AreEqual(64, result.PreviousSha256.Length);

        using var manifest = JsonDocument.Parse(File.ReadAllText(result.VersionManifestPath));
        var root = manifest.RootElement;
        Assert.AreEqual("development", root.GetProperty("channel").GetString());
        Assert.AreEqual(
            "development-20260826T230000Z-0123456789ab",
            root.GetProperty("release").GetProperty("tag").GetString());
        var windows = root.GetProperty("platforms").GetProperty("windows-x64");
        Assert.AreEqual("self-contained", windows.GetProperty("runtime").GetProperty("kind").GetString());
        Assert.AreEqual("0.1.0-alpha.1", windows.GetProperty("identity").GetProperty("version").GetString());
        Assert.AreEqual(Commit, windows.GetProperty("identity").GetProperty("commit").GetString());
        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture.PackagePath))).ToLowerInvariant(),
            windows.GetProperty("archive").GetProperty("sha256").GetString());

        using var catalog = JsonDocument.Parse(File.ReadAllText(result.ReleaseCatalogPath));
        var development = catalog.RootElement.GetProperty("development");
        Assert.AreEqual(10, development.GetArrayLength());
        Assert.AreEqual(
            "development-20260826T230000Z-0123456789ab",
            development[0].GetProperty("tag").GetString());
        Assert.AreEqual(
            "development-20260826T140000Z-0123456789ab",
            development[9].GetProperty("tag").GetString());
        Assert.AreEqual(1, catalog.RootElement.GetProperty("accepted").GetArrayLength());
    }

    [TestMethod]
    public void Development_manifest_generation_fails_closed_for_mutable_or_framework_dependent_inputs()
    {
        using (var frameworkDependent = new DevelopmentManifestFixture(selfContained: false))
        {
            Assert.ThrowsExactly<InvalidDataException>(() =>
                DevelopmentManifestBuilder.Build(
                    frameworkDependent.Request("development-20260826T120000Z-0123456789ab", 12)));
        }
        using (var sensitive = new DevelopmentManifestFixture(
                   selfContained: true,
                   includeSensitiveFile: true))
        {
            Assert.ThrowsExactly<InvalidDataException>(() =>
                DevelopmentManifestBuilder.Build(
                    sensitive.Request("development-20260826T120000Z-0123456789ab", 12)));
        }

        using var fixture = new DevelopmentManifestFixture(selfContained: true);
        var request = fixture.Request("development-20260826T130000Z-0123456789ab", 13);
        var result = DevelopmentManifestBuilder.Build(request);
        File.WriteAllText(result.VersionManifestPath, "{}\n");

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DevelopmentManifestBuilder.Build(request));
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
                "Open Balls.cmd",
            },
            archive.Entries.Select(entry => entry.FullName).ToArray());

        var readme = ReadText(archive, "README.md");
        StringAssert.Contains(readme, "Open Balls.cmd");

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
    public void Linux_package_is_a_runnable_development_Canary()
    {
        using var fixture = new PackageFixture("balls", "ballsd");

        var result = CanaryPackageBuilder.Build(fixture.Request(CanaryPlatform.Linux));

        Assert.AreEqual(
            "balls-0.1.0-alpha.1-canary-linux-x64-0123456789ab",
            result.ArtifactName);
        Assert.IsTrue(File.Exists(result.InstallerPath));
        using var archive = ZipFile.OpenRead(result.ArchivePath);
        Assert.IsNotNull(archive.GetEntry("Install-BallsCanary.sh"));
        using var manifest = ReadJson(archive, "canary.json");
        Assert.IsTrue(manifest.RootElement.GetProperty("runtimeSupported").GetBoolean());
        var readme = ReadText(archive, "README.md");
        StringAssert.Contains(readme, "Balls Linux Canary");
        StringAssert.Contains(readme, "Unix-domain socket");
        StringAssert.Contains(readme, "bash ./Install-BallsCanary.sh");
        AssertInternalChecksums(archive);
    }

    [TestMethod]
    public void Package_rejects_a_non_commit_identity()
    {
        using var fixture = new PackageFixture("balls.exe", "ballsd.exe");
        var request = fixture.Request(CanaryPlatform.Windows) with { Commit = "main" };

        Assert.ThrowsExactly<ArgumentException>(() => CanaryPackageBuilder.Build(request));
    }

    [TestMethod]
    public void Windows_launcher_uses_a_detached_process_with_actionable_startup_logs()
    {
        var launcher = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "canary",
            "Open-Balls.cmd"));

        StringAssert.Contains(launcher, "Start-Process -FilePath $env:BALLS_DAEMON");
        StringAssert.Contains(launcher, "set \"BALLS_HOME=%LOCALAPPDATA%\\Balls\"");
        StringAssert.Contains(launcher, "set \"BALLS_PIPE=balls\"");
        StringAssert.Contains(launcher, "-RedirectStandardOutput $env:BALLS_STDOUT");
        StringAssert.Contains(launcher, "-RedirectStandardError $env:BALLS_STDERR");
        StringAssert.Contains(launcher, "Startup log: %BALLS_STDERR%");
        Assert.IsFalse(
            launcher.Contains("start \"Balls background node\"", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Windows_download_smoke_checks_exact_identity_shortcut_policy_and_owned_cleanup()
    {
        var smoke = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "canary",
            "Test-WindowsDownload.ps1"));

        StringAssert.Contains(smoke, "installation.json");
        StringAssert.Contains(smoke, "$record.release.tag -cne $ExpectedTag");
        StringAssert.Contains(smoke, "$record.release.commit -cne $ExpectedCommit");
        StringAssert.Contains(smoke, "$daemon.Path");
        StringAssert.Contains(smoke, "WScript.Shell");
        StringAssert.Contains(smoke, "executionPolicyUnchanged = $true");
        StringAssert.Contains(smoke, "Refusing to remove a Balls shortcut not owned by this smoke run.");
        Assert.IsFalse(smoke.Contains("Set-ExecutionPolicy", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(smoke.Contains("ExecutionPolicy Bypass", StringComparison.OrdinalIgnoreCase));
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Balls.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Balls.slnx from the test directory.");
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
            File.WriteAllText(
                Path.Combine(RepositoryRoot, "eng", "canary", "Install-BallsCanary.sh"),
                "#!/usr/bin/env bash");
            File.WriteAllText(
                Path.Combine(RepositoryRoot, "eng", "canary", "Open-Balls.cmd"),
                "@echo off");
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

    private sealed class DevelopmentManifestFixture : IDisposable
    {
        private readonly string root;

        public DevelopmentManifestFixture(bool selfContained, bool includeSensitiveFile = false)
        {
            root = Path.Combine(Path.GetTempPath(), $"balls-development-manifest-tests-{Guid.NewGuid():N}");
            PublicRoot = Path.Combine(root, "public");
            Directory.CreateDirectory(PublicRoot);
            File.WriteAllText(
                Path.Combine(PublicRoot, "releases.json"),
                """
                {
                  "schemaVersion": 1,
                  "accepted": [
                    {
                      "tag": "0.1.0-alpha.1",
                      "publishedAt": "2026-08-20T12:00:00Z",
                      "manifest": "/versions/0.1.0-alpha.1.json"
                    }
                  ],
                  "development": [],
                  "completeHistory": "https://github.com/scwlkr/balls/releases"
                }
                """ + Environment.NewLine,
                new UTF8Encoding(false));

            PackagePath = Path.Combine(
                root,
                "balls-0.1.0-alpha.1-canary-windows-x64-0123456789ab.zip");
            using (var archive = ZipFile.Open(PackagePath, ZipArchiveMode.Create))
            {
                WriteArchiveText(
                    archive,
                    "canary.json",
                    $$"""
                    {
                      "product": "Balls",
                      "version": "0.1.0-alpha.1",
                      "commit": "{{Commit}}",
                      "platform": "windows",
                      "architecture": "x64",
                      "runtimeSupported": true
                    }
                    """);
                var runtimeProperty = selfContained
                    ? "\"includedFrameworks\": [{ \"name\": \"Microsoft.NETCore.App\", \"version\": \"10.0.0\" }]"
                    : "\"frameworks\": [{ \"name\": \"Microsoft.NETCore.App\", \"version\": \"10.0.0\" }]";
                var runtimeConfig = $"{{ \"runtimeOptions\": {{ {runtimeProperty} }} }}";
                WriteArchiveText(archive, "balls/balls.runtimeconfig.json", runtimeConfig);
                WriteArchiveText(archive, "ballsd/ballsd.runtimeconfig.json", runtimeConfig);
                if (includeSensitiveFile)
                {
                    WriteArchiveText(archive, ".env", "CLIENT_SECRET='not-a-real-fixture-secret'");
                }
            }

            var archiveHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(PackagePath)));
            ChecksumPath = $"{PackagePath}.sha256";
            File.WriteAllText(
                ChecksumPath,
                $"{archiveHash}  {Path.GetFileName(PackagePath)}{Environment.NewLine}",
                new UTF8Encoding(false));
            InstallerPath = Path.Combine(root, "Install-BallsCanary.ps1");
            File.WriteAllText(InstallerPath, "# test installer\n", new UTF8Encoding(false));
        }

        public string PublicRoot { get; }

        public string PackagePath { get; }

        public string ChecksumPath { get; }

        public string InstallerPath { get; }

        public DevelopmentManifestRequest Request(string tag, int hour) => new(
            PublicRoot,
            PackagePath,
            ChecksumPath,
            InstallerPath,
            tag,
            Commit,
            $"2026-08-26T{hour:D2}:00:00Z");

        public void Dispose()
        {
            Directory.Delete(root, recursive: true);
        }

        private static void WriteArchiveText(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }
}
