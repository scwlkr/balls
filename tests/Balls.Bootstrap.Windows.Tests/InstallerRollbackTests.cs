using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Balls.Bootstrap.Windows;

namespace Balls.Bootstrap.Windows.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class InstallerRollbackTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [TestMethod]
    public async Task Daemon_start_failure_restores_owned_files_and_removes_the_new_version()
    {
        var root = Path.Combine(Path.GetTempPath(), $"balls-bootstrap-rollback-{Guid.NewGuid():N}");
        var installRoot = Path.Combine(root, "install");
        var packagePath = Path.Combine(root, "balls-0.3.0-alpha.1-canary-windows-x64-0123456789ab.zip");
        var checksumPath = packagePath + ".sha256";
        var launcherPath = Path.Combine(installRoot, "launchers", "0.3.0-alpha.1-0123456789ab.cmd");
        var recordPath = Path.Combine(installRoot, "installation.json");
        try
        {
            Directory.CreateDirectory(root);
            WriteInvalidExecutablePackage(packagePath);
            var hash = PackageVerifier.HashFile(packagePath);
            await File.WriteAllTextAsync(
                checksumPath,
                $"{hash.ToUpperInvariant()}  {Path.GetFileName(packagePath)}\n",
                new UTF8Encoding(false));
            Directory.CreateDirectory(Path.GetDirectoryName(launcherPath)!);
            await File.WriteAllTextAsync(launcherPath, "prior launcher", new UTF8Encoding(false));
            await File.WriteAllTextAsync(recordPath, "prior record", new UTF8Encoding(false));

            var options = new BootstrapOptions(
                null,
                packagePath,
                checksumPath,
                installRoot,
                "balls-test",
                "Balls Test Node",
                OpenUi: false,
                CreateShortcut: false);
            using var installer = new WindowsBootstrapInstaller();

            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                installer.InstallAsync(options, CancellationToken.None));

            StringAssert.Contains(error.Message, "Windows did not allow Balls to start");
            Assert.AreEqual("prior launcher", await File.ReadAllTextAsync(launcherPath));
            Assert.AreEqual("prior record", await File.ReadAllTextAsync(recordPath));
            Assert.IsFalse(File.Exists(Path.Combine(installRoot, "ballsd.pid")));
            Assert.IsFalse(Directory.Exists(Path.Combine(
                installRoot,
                "versions",
                "0.3.0-alpha.1-0123456789ab")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void WriteInvalidExecutablePackage(string packagePath)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["canary.json"] = $$"""
                {
                  "product": "Balls",
                  "version": "0.3.0-alpha.1",
                  "commit": "{{Commit}}",
                  "platform": "windows",
                  "architecture": "x64",
                  "runtimeSupported": true,
                  "support": "rollback test"
                }
                """,
            ["balls/balls.exe"] = "not an executable",
            ["ballsd/ballsd.exe"] = "not an executable",
        };
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        foreach (var pair in files)
        {
            Write(archive, pair.Key, pair.Value);
        }
        var checksums = files
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
                $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Value)))}  {pair.Key}");
        Write(archive, "SHA256SUMS", string.Join('\n', checksums) + "\n");
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
