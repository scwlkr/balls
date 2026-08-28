using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Balls.WindowsConformance;

internal sealed record WindowsPackageIdentity(
    string Path,
    string FileName,
    string Sha256,
    string Commit,
    string Version,
    string Architecture);

internal sealed record WindowsPackageManifest(
    string Product,
    string Version,
    string Commit,
    string Platform,
    string Architecture,
    bool RuntimeSupported,
    string Support);

internal static partial class WindowsPackageIdentityLoader
{
    private const long MaximumPackageBytes = 1024L * 1024 * 1024;
    private const int MaximumManifestBytes = 16 * 1024;

    public static WindowsPackageIdentity Load(
        string packagePath,
        string checksumPath,
        string expectedCommit)
    {
        if (!CommitPattern().IsMatch(expectedCommit))
        {
            throw new ConformanceRefusalException("expected_commit_invalid");
        }

        var package = RequireRegularFile(packagePath, MaximumPackageBytes);
        var checksum = RequireRegularFile(checksumPath, 1024);
        var checksumMatch = ChecksumPattern().Match(File.ReadAllText(checksum.FullName));
        if (!checksumMatch.Success
            || checksumMatch.Groups[2].Value != package.Name)
        {
            throw new ConformanceRefusalException("package_checksum_invalid");
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package.FullName)));
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHash),
                Convert.FromHexString(checksumMatch.Groups[1].Value)))
        {
            throw new ConformanceRefusalException("package_hash_mismatch");
        }

        WindowsPackageManifest? manifest;
        try
        {
            using var archive = ZipFile.OpenRead(package.FullName);
            var entries = archive.Entries
                .Where(entry => entry.FullName == "canary.json")
                .ToArray();
            if (entries.Length != 1 || entries[0].Length is <= 0 or > MaximumManifestBytes)
            {
                throw new ConformanceRefusalException("package_manifest_invalid");
            }

            using var stream = entries[0].Open();
            manifest = JsonSerializer.Deserialize<WindowsPackageManifest>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    RespectNullableAnnotations = true,
                    RespectRequiredConstructorParameters = true,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                });
        }
        catch (InvalidDataException)
        {
            throw new ConformanceRefusalException("package_manifest_invalid");
        }
        catch (JsonException)
        {
            throw new ConformanceRefusalException("package_manifest_invalid");
        }

        if (manifest is null
            || manifest.Product != "Balls"
            || manifest.Platform != "windows"
            || manifest.Architecture != "x64"
            || !manifest.RuntimeSupported
            || manifest.Support != "Windows Canary for development use."
            || !CommitPattern().IsMatch(manifest.Commit)
            || !string.Equals(manifest.Commit, expectedCommit, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(manifest.Version)
            || manifest.Version.Length > 64)
        {
            throw new ConformanceRefusalException("product_identity_mismatch");
        }

        return new WindowsPackageIdentity(
            package.FullName,
            package.Name,
            actualHash,
            manifest.Commit.ToLowerInvariant(),
            manifest.Version,
            manifest.Architecture);
    }

    private static FileInfo RequireRegularFile(string path, long maximumBytes)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists
            || file.Length is <= 0
            || file.Length > maximumBytes
            || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ConformanceRefusalException("package_input_invalid");
        }

        return file;
    }

    [GeneratedRegex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex("\\A([0-9a-fA-F]{64})  ([^/\\\\\\r\\n]+)\\r?\\n?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ChecksumPattern();
}
