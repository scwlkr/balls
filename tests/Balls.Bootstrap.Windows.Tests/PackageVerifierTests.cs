using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Balls.Bootstrap.Windows;

namespace Balls.Bootstrap.Windows.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class PackageVerifierTests
{
    [TestMethod]
    public void Exact_package_is_extracted_and_every_file_is_bound()
    {
        using var fixture = new PackageFixture();

        PackageVerifier.ReadAndValidateIdentity(fixture.ArchivePath, fixture.Identity);
        PackageVerifier.ExtractAndValidate(fixture.ArchivePath, fixture.ExtractRoot);

        Assert.IsTrue(File.Exists(Path.Combine(fixture.ExtractRoot, "balls", "balls.exe")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.ExtractRoot, "ballsd", "ballsd.exe")));
        Assert.AreEqual(fixture.Identity, PackageVerifier.ReadInstalledIdentity(fixture.ExtractRoot));
    }

    [TestMethod]
    public void Traversal_and_unhashed_files_fail_closed()
    {
        using (var fixture = new PackageFixture("../escape.txt"))
        {
            Assert.ThrowsExactly<InvalidDataException>(() =>
                PackageVerifier.ExtractAndValidate(fixture.ArchivePath, fixture.ExtractRoot));
        }
        using (var fixture = new PackageFixture("extra.txt", includeInChecksums: false))
        {
            Assert.ThrowsExactly<InvalidDataException>(() =>
                PackageVerifier.ExtractAndValidate(fixture.ArchivePath, fixture.ExtractRoot));
        }
    }

    private sealed class PackageFixture : IDisposable
    {
        private const string Commit = "0123456789abcdef0123456789abcdef01234567";
        private readonly string root;

        public PackageFixture(string? extraPath = null, bool includeInChecksums = true)
        {
            root = Path.Combine(Path.GetTempPath(), $"balls-bootstrap-package-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            ArchivePath = Path.Combine(root, "package.zip");
            ExtractRoot = Path.Combine(root, "extract");
            Identity = new PackageIdentity("Balls", "0.3.0-alpha.1", Commit, "windows", "x64");

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
                      "support": "test"
                    }
                    """,
                ["balls/balls.exe"] = "cli",
                ["ballsd/ballsd.exe"] = "daemon",
            };
            if (extraPath is not null)
            {
                files[extraPath] = "extra";
            }

            using var archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Create);
            foreach (var pair in files)
            {
                Write(archive, pair.Key, pair.Value);
            }
            var checksumFiles = includeInChecksums || extraPath is null
                ? files
                : files.Where(pair => !string.Equals(pair.Key, extraPath, StringComparison.Ordinal))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var checksums = checksumFiles
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Value)))}  {pair.Key}");
            Write(archive, "SHA256SUMS", string.Join('\n', checksums) + "\n");
        }

        public string ArchivePath { get; }
        public string ExtractRoot { get; }
        public PackageIdentity Identity { get; }

        public void Dispose() => Directory.Delete(root, recursive: true);

        private static void Write(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }
}
