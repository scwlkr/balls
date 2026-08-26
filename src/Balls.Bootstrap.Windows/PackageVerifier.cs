using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Balls.Bootstrap.Windows;

internal static partial class PackageVerifier
{
    private const int MaximumEntries = 10_000;
    private const long MaximumExpandedBytes = 2_147_483_648;

    public static PackageIdentity ReadAndValidateIdentity(
        string packagePath,
        PackageIdentity expectedIdentity)
    {
        var actual = ReadIdentity(packagePath);
        if (actual != expectedIdentity)
        {
            throw new InvalidDataException("The Windows package identity does not match the selected Balls manifest.");
        }
        return actual;
    }

    public static PackageIdentity ReadIdentity(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => string.Equals(entry.FullName, "canary.json", StringComparison.Ordinal))
            .ToArray();
        if (entries.Length != 1 || entries[0].Length is <= 0 or > 65_536)
        {
            throw new InvalidDataException("The Windows package does not contain one bounded package manifest.");
        }

        PackageManifest document;
        try
        {
            using var stream = entries[0].Open();
            document = JsonSerializer.Deserialize<PackageManifest>(stream, JsonOptions)
                ?? throw new InvalidDataException("The Windows package manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Windows package manifest is not valid JSON.", exception);
        }

        return ValidateIdentity(document);
    }

    public static PackageIdentity ReadInstalledIdentity(string packageRoot)
    {
        var manifestPath = Path.Combine(packageRoot, "canary.json");
        if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length is <= 0 or > 65_536)
        {
            throw new InvalidDataException("The installed Windows package manifest is missing or unsafe.");
        }
        try
        {
            using var stream = File.OpenRead(manifestPath);
            var document = JsonSerializer.Deserialize<PackageManifest>(stream, JsonOptions)
                ?? throw new InvalidDataException("The installed Windows package manifest is empty.");
            return ValidateIdentity(document);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The installed Windows package manifest is not valid JSON.", exception);
        }
    }

    private static PackageIdentity ValidateIdentity(PackageManifest document)
    {
        var actual = new PackageIdentity(
            document.Product,
            document.Version,
            document.Commit,
            document.Platform,
            document.Architecture);
        if (!document.RuntimeSupported ||
            !string.Equals(actual.Product, "Balls", StringComparison.Ordinal) ||
            !VersionPattern().IsMatch(actual.Version) ||
            !CommitPattern().IsMatch(actual.Commit) ||
            !string.Equals(actual.Platform, "windows", StringComparison.Ordinal) ||
            !string.Equals(actual.Architecture, "x64", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Windows package has an invalid identity.");
        }
        return actual;
    }

    public static void ValidateChecksumBinding(string checksumText, ReleaseAsset archive)
    {
        var match = ChecksumLinePattern().Match(checksumText.Trim());
        if (!match.Success ||
            !string.Equals(match.Groups[1].Value, archive.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(match.Groups[2].Value, archive.Name, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The package checksum file does not bind the selected Windows archive.");
        }
    }

    public static void ExtractAndValidate(string packagePath, string destination)
    {
        var destinationRoot = Path.GetFullPath(destination);
        Directory.CreateDirectory(destinationRoot);
        var destinationPrefix = destinationRoot + Path.DirectorySeparatorChar;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;

        using (var archive = ZipFile.OpenRead(packagePath))
        {
            if (archive.Entries.Count is <= 0 or > MaximumEntries)
            {
                throw new InvalidDataException("The Windows package has an unsafe number of archive entries.");
            }

            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.Length is <= 0 or > 240 || entry.FullName.Contains('\\'))
                {
                    throw new InvalidDataException("The Windows package contains an invalid archive path.");
                }
                var target = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
                if (!target.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase) ||
                    !paths.Add(target))
                {
                    throw new InvalidDataException("The Windows package contains a duplicate or escaping archive path.");
                }
                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > MaximumExpandedBytes)
                {
                    throw new InvalidDataException("The Windows package expands beyond the supported size.");
                }
            }

            archive.ExtractToDirectory(destinationRoot, overwriteFiles: false);
        }

        ValidateInternalChecksums(destinationRoot);
        foreach (var relativePath in new[] { "balls/balls.exe", "ballsd/ballsd.exe" })
        {
            var path = Path.Combine(destinationRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                throw new InvalidDataException($"The Windows package is missing {relativePath}.");
            }
        }
    }

    public static void ValidateInternalChecksums(string packageRoot)
    {
        var root = Path.GetFullPath(packageRoot);
        var rootPrefix = root + Path.DirectorySeparatorChar;
        var checksumPath = Path.Combine(root, "SHA256SUMS");
        if (!File.Exists(checksumPath))
        {
            throw new InvalidDataException("The Windows package is missing its internal checksum manifest.");
        }

        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(checksumPath))
        {
            var match = InternalChecksumPattern().Match(line);
            if (!match.Success || !expected.TryAdd(match.Groups[2].Value, match.Groups[1].Value))
            {
                throw new InvalidDataException("The Windows package contains an invalid internal checksum entry.");
            }
            var relative = match.Groups[2].Value.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
            {
                throw new InvalidDataException("An internal checksum path escapes or is missing from the package.");
            }
            using var stream = File.OpenRead(target);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actual, match.Groups[1].Value, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Internal checksum mismatch: {match.Groups[2].Value}");
            }
        }

        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(file, checksumPath, StringComparison.Ordinal))
            {
                continue;
            }
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!expected.ContainsKey(relative))
            {
                throw new InvalidDataException($"The Windows package contains an unhashed file: {relative}");
            }
        }
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
    };

    private sealed class PackageManifest
    {
        public required string Product { get; init; }
        public required string Version { get; init; }
        public required string Commit { get; init; }
        public required string Platform { get; init; }
        public required string Architecture { get; init; }
        public bool RuntimeSupported { get; init; }
        public required string Support { get; init; }
    }

    [GeneratedRegex("^([0-9A-Fa-f]{64})  ([0-9A-Za-z][0-9A-Za-z._-]{0,191})$", RegexOptions.CultureInvariant)]
    private static partial Regex ChecksumLinePattern();

    [GeneratedRegex("^([0-9A-F]{64})  ([^\\r\\n]{1,240})$", RegexOptions.CultureInvariant)]
    private static partial Regex InternalChecksumPattern();

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();
}
