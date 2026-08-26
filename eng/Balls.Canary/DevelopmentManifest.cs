using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Balls.Canary;

internal sealed record DevelopmentManifestResult(
    string VersionManifestPath,
    string ChannelManifestPath,
    string ReleaseCatalogPath,
    string? PreviousTag,
    string? PreviousSha256);

internal static partial class DevelopmentManifestBuilder
{
    private const string ReleasesUrl = "https://github.com/scwlkr/balls/releases";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static DevelopmentManifestResult Build(DevelopmentManifestRequest request)
    {
        ValidateRequest(request);

        var publicRoot = Path.GetFullPath(request.PublicRoot);
        var packagePath = Path.GetFullPath(request.PackagePath);
        var checksumPath = Path.GetFullPath(request.ChecksumPath);
        var installerPath = Path.GetFullPath(request.InstallerPath);
        var identity = ReadPackageIdentity(packagePath, request.Commit);
        RequireNoSensitivePayload(packagePath);
        RequireSelfContainedRuntime(packagePath);

        var archiveName = Path.GetFileName(packagePath);
        var expectedArchiveName =
            $"balls-{identity.Version}-canary-windows-x64-{request.Commit[..12]}.zip";
        if (!string.Equals(archiveName, expectedArchiveName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Windows archive name must match its internal identity: {expectedArchiveName}");
        }

        var archiveSha256 = HashFile(packagePath);
        var checksumName = Path.GetFileName(checksumPath);
        if (!string.Equals(checksumName, $"{archiveName}.sha256", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The checksum filename does not match the Windows archive.");
        }
        var checksumLine = File.ReadAllText(checksumPath).Trim();
        if (!string.Equals(
                checksumLine,
                $"{archiveSha256.ToUpperInvariant()}  {archiveName}",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The checksum file does not bind the exact Windows archive.");
        }

        var installerName = Path.GetFileName(installerPath);
        if (!string.Equals(installerName, "Install-BallsCanary.ps1", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Windows installer has an unexpected filename.");
        }

        var publishedAt = DateTimeOffset.ParseExact(
            request.PublishedAt,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var releaseRoot = $"https://github.com/scwlkr/balls/releases/download/{request.Tag}";
        var manifest = new
        {
            schemaVersion = 1,
            channel = "development",
            release = new
            {
                tag = request.Tag,
                commit = request.Commit,
                publishedAt = publishedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                url = $"{ReleasesUrl}/tag/{request.Tag}",
                unsigned = true,
            },
            platforms = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["windows-x64"] = new
                {
                    delivery = "package",
                    identity = new
                    {
                        product = identity.Product,
                        version = identity.Version,
                        commit = identity.Commit,
                        platform = identity.Platform,
                        architecture = identity.Architecture,
                    },
                    runtime = new { kind = "self-contained", architecture = "x64" },
                    archive = Asset(archiveName, releaseRoot, archiveSha256),
                    checksum = Asset(checksumName, releaseRoot, HashFile(checksumPath)),
                    installer = Asset(installerName, releaseRoot, HashFile(installerPath)),
                },
            },
        };
        var manifestText = Serialize(manifest);

        var versionsRoot = Path.Combine(publicRoot, "versions");
        var channelsRoot = Path.Combine(publicRoot, "channels");
        var versionManifestPath = Path.Combine(versionsRoot, $"{request.Tag}.json");
        var channelManifestPath = Path.Combine(channelsRoot, "development.json");
        var releaseCatalogPath = Path.Combine(publicRoot, "releases.json");
        var catalogText = BuildCatalog(releaseCatalogPath, request.Tag, publishedAt);

        string? previousTag = null;
        string? previousSha256 = null;
        if (File.Exists(channelManifestPath))
        {
            var previousBytes = File.ReadAllBytes(channelManifestPath);
            previousSha256 = Convert.ToHexString(SHA256.HashData(previousBytes)).ToLowerInvariant();
            using var previous = JsonDocument.Parse(previousBytes);
            previousTag = previous.RootElement
                .GetProperty("release")
                .GetProperty("tag")
                .GetString();
        }

        Directory.CreateDirectory(versionsRoot);
        Directory.CreateDirectory(channelsRoot);
        if (File.Exists(versionManifestPath))
        {
            var existing = File.ReadAllText(versionManifestPath);
            if (!string.Equals(existing, manifestText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Immutable version manifest already exists with different content: {versionManifestPath}");
            }
        }
        else
        {
            File.WriteAllText(versionManifestPath, manifestText, new UTF8Encoding(false));
        }

        WriteAtomic(channelManifestPath, manifestText);
        WriteAtomic(releaseCatalogPath, catalogText);
        return new DevelopmentManifestResult(
            versionManifestPath,
            channelManifestPath,
            releaseCatalogPath,
            previousTag,
            previousSha256);
    }

    private static void ValidateRequest(DevelopmentManifestRequest request)
    {
        RequireDirectory(request.PublicRoot, nameof(request.PublicRoot));
        RequireFile(request.PackagePath);
        RequireFile(request.ChecksumPath);
        RequireFile(request.InstallerPath);
        RequireFile(Path.Combine(request.PublicRoot, "releases.json"));

        if (!CommitPattern().IsMatch(request.Commit))
        {
            throw new ArgumentException("Commit must be a full lowercase SHA-1 identity.", nameof(request));
        }
        if (!DateTimeOffset.TryParseExact(
                request.PublishedAt,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var publishedAt))
        {
            throw new ArgumentException(
                "Published timestamp must be UTC in yyyy-MM-ddTHH:mm:ssZ form.",
                nameof(request));
        }
        if (!TagPattern().IsMatch(request.Tag) ||
            !request.Tag.EndsWith($"-{request.Commit[..12]}", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Development tag must use development-<build-UTC>-<commit12> and match the package commit.",
                nameof(request));
        }
    }

    private static PackageIdentity ReadPackageIdentity(string packagePath, string expectedCommit)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => string.Equals(entry.FullName, "canary.json", StringComparison.Ordinal))
            .ToArray();
        if (entries.Length != 1 || entries[0].Length > 65536)
        {
            throw new InvalidDataException("The Windows archive must contain one bounded canary.json.");
        }

        using var stream = entries[0].Open();
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var identity = new PackageIdentity(
            root.GetProperty("product").GetString() ?? string.Empty,
            root.GetProperty("version").GetString() ?? string.Empty,
            root.GetProperty("commit").GetString() ?? string.Empty,
            root.GetProperty("platform").GetString() ?? string.Empty,
            root.GetProperty("architecture").GetString() ?? string.Empty);
        if (!string.Equals(identity.Product, "Balls", StringComparison.Ordinal) ||
            !VersionPattern().IsMatch(identity.Version) ||
            !string.Equals(identity.Commit, expectedCommit, StringComparison.Ordinal) ||
            !string.Equals(identity.Platform, "windows", StringComparison.Ordinal) ||
            !string.Equals(identity.Architecture, "x64", StringComparison.Ordinal) ||
            !root.GetProperty("runtimeSupported").GetBoolean())
        {
            throw new InvalidDataException("The archive does not have the expected Windows package identity.");
        }
        return identity;
    }

    private static void RequireSelfContainedRuntime(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var path in new[] { "balls/balls.runtimeconfig.json", "ballsd/ballsd.runtimeconfig.json" })
        {
            var entry = archive.GetEntry(path) ??
                throw new InvalidDataException($"Self-contained Windows archive is missing {path}.");
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            var runtimeOptions = document.RootElement.GetProperty("runtimeOptions");
            if (!runtimeOptions.TryGetProperty("includedFrameworks", out var frameworks) ||
                frameworks.ValueKind != JsonValueKind.Array || frameworks.GetArrayLength() == 0)
            {
                throw new InvalidDataException(
                    $"Windows archive is not self-contained according to {path}.");
            }
        }
    }

    private static void RequireNoSensitivePayload(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var utf8 = new UTF8Encoding(false, true);
        foreach (var entry in archive.Entries.Where(entry => entry.Name.Length > 0))
        {
            var normalizedName = entry.FullName.Replace('\\', '/');
            if (SensitiveNamePattern().IsMatch(normalizedName))
            {
                throw new InvalidDataException(
                    $"Windows archive contains a forbidden sensitive file: {normalizedName}");
            }

            var extension = Path.GetExtension(entry.Name);
            if (entry.Length > 1_048_576 || !TextExtensions.Contains(extension))
            {
                continue;
            }
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream, utf8, detectEncodingFromByteOrderMarks: true);
                var text = reader.ReadToEnd();
                if (SensitiveContentPattern().IsMatch(text))
                {
                    throw new InvalidDataException(
                        $"Windows archive contains credential-like text in: {normalizedName}");
                }
            }
            catch (DecoderFallbackException)
            {
                // A file with a text-like extension may still be binary; other identity checks remain authoritative.
            }
        }
    }

    private static string BuildCatalog(
        string releaseCatalogPath,
        string tag,
        DateTimeOffset publishedAt)
    {
        var root = JsonNode.Parse(File.ReadAllText(releaseCatalogPath))?.AsObject() ??
            throw new InvalidDataException("Release catalog is not a JSON object.");
        if (root["schemaVersion"]?.GetValue<int>() != 1 ||
            root["accepted"] is not JsonArray ||
            root["development"] is not JsonArray development ||
            root["completeHistory"]?.GetValue<string>() != ReleasesUrl)
        {
            throw new InvalidDataException("Release catalog has an unsupported contract.");
        }

        for (var index = development.Count - 1; index >= 0; index--)
        {
            if (development[index]?["tag"]?.GetValue<string>() == tag)
            {
                development.RemoveAt(index);
            }
        }
        development.Insert(
            0,
            new JsonObject
            {
                ["tag"] = tag,
                ["publishedAt"] = publishedAt.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    CultureInfo.InvariantCulture),
                ["manifest"] = $"/versions/{tag}.json",
                ["knownBroken"] = true,
                ["note"] = "Development test build; it may be incomplete or broken.",
            });
        while (development.Count > 10)
        {
            development.RemoveAt(development.Count - 1);
        }
        return root.ToJsonString(JsonOptions) + Environment.NewLine;
    }

    private static object Asset(string name, string releaseRoot, string sha256) => new
    {
        name,
        url = $"{releaseRoot}/{name}",
        sha256,
    };

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine;

    private static void WriteAtomic(string path, string content)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
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
            throw new FileNotFoundException("Required Development manifest input not found.", path);
        }
    }

    private sealed record PackageIdentity(
        string Product,
        string Version,
        string Commit,
        string Platform,
        string Architecture);

    private static readonly HashSet<string> TextExtensions = new(
        [".cmd", ".config", ".json", ".md", ".ps1", ".sh", ".txt", ".xml", ".yaml", ".yml"],
        StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(
        "^development-[0-9]{8}T[0-9]{6}Z-[0-9a-f]{12}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex(
        "(^|/)(\\.env($|\\.)|id_(rsa|dsa|ecdsa|ed25519)$|[^/]*\\.(key|pem|pfx|p12)$|credentials?($|\\.)|secrets?($|\\.)|[^/]*\\.(db|sqlite)(3)?$)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveNamePattern();

    [GeneratedRegex(
        "github_pat_[0-9A-Za-z_]{20,}|gh[pousr]_[0-9A-Za-z]{20,}|AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----|[\\\"']?(password|access[_-]?token|client[_-]?secret)[\\\"']?\\s*[:=]\\s*[\\\"'][^\\\"']{8,}",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveContentPattern();
}
