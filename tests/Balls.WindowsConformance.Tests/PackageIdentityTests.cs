using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Balls.WindowsConformance;

namespace Balls.WindowsConformance.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class PackageIdentityTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [TestMethod]
    public void Hash_bound_Windows_package_selects_the_exact_commit()
    {
        using var package = PackageFixture.Create(Commit);

        var identity = WindowsPackageIdentityLoader.Load(
            package.PackagePath,
            package.ChecksumPath,
            Commit);

        Assert.AreEqual(Commit, identity.Commit);
        Assert.AreEqual("windows", ReadPlatform(package.PackagePath));
        Assert.AreEqual("x64", identity.Architecture);
        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package.PackagePath))),
            identity.Sha256);
    }

    [TestMethod]
    public void Package_hash_mismatch_fails_closed()
    {
        using var package = PackageFixture.Create(Commit);
        File.AppendAllText(package.PackagePath, "changed");

        var exception = Assert.ThrowsExactly<ConformanceRefusalException>(() =>
            WindowsPackageIdentityLoader.Load(
                package.PackagePath,
                package.ChecksumPath,
                Commit));

        Assert.AreEqual("package_hash_mismatch", exception.Code);
    }

    [TestMethod]
    public void Product_commit_mismatch_fails_closed()
    {
        using var package = PackageFixture.Create(Commit);

        var exception = Assert.ThrowsExactly<ConformanceRefusalException>(() =>
            WindowsPackageIdentityLoader.Load(
                package.PackagePath,
                package.ChecksumPath,
                "abcdef0123456789abcdef0123456789abcdef01"));

        Assert.AreEqual("product_identity_mismatch", exception.Code);
    }

    private static string ReadPlatform(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        using var manifest = JsonDocument.Parse(archive.GetEntry("canary.json")!.Open());
        return manifest.RootElement.GetProperty("platform").GetString()!;
    }
}

internal sealed class PackageFixture : IDisposable
{
    private PackageFixture(string directory, string packagePath, string checksumPath)
    {
        Directory = directory;
        PackagePath = packagePath;
        ChecksumPath = checksumPath;
    }

    public string Directory { get; }

    public string PackagePath { get; }

    public string ChecksumPath { get; }

    public static PackageFixture Create(string commit)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"balls-conformance-package-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(directory);
        var packagePath = Path.Combine(directory, "balls-test-windows-x64.zip");
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("canary.json");
            using var stream = entry.Open();
            JsonSerializer.Serialize(stream, new
            {
                product = "Balls",
                version = "0.3.0-alpha.1",
                commit,
                platform = "windows",
                architecture = "x64",
                runtimeSupported = true,
                support = "Windows Canary for development use.",
            });
        }

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath)));
        var checksumPath = packagePath + ".sha256";
        File.WriteAllText(checksumPath, $"{hash}  {Path.GetFileName(packagePath)}\n");
        return new PackageFixture(directory, packagePath, checksumPath);
    }

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
}
