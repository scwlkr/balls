using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Balls.Bootstrap.Windows;

internal sealed record ValidatedRelease(
    string Channel,
    string Tag,
    string Commit,
    PackageIdentity Identity,
    RuntimeContract Runtime,
    ReleaseAsset Archive,
    ReleaseAsset Checksum,
    ReleaseAsset Installer);

internal sealed record PackageIdentity(
    string Product,
    string Version,
    string Commit,
    string Platform,
    string Architecture);

internal sealed record RuntimeContract(
    string Kind,
    string Architecture,
    IReadOnlyList<RuntimeFramework> Frameworks);

internal sealed record RuntimeFramework(string Name, int Major);

internal sealed record ReleaseAsset(string Name, Uri Url, string Sha256);

internal static partial class ReleaseManifestReader
{
    private const int MaximumManifestBytes = 262_144;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
    };

    public static ValidatedRelease Read(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumManifestBytes)
        {
            throw new InvalidDataException("The Balls manifest has an unsafe size.");
        }

        ReleaseManifestDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ReleaseManifestDocument>(bytes.Span, JsonOptions)
                ?? throw new InvalidDataException("The Balls manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Balls manifest is not valid JSON.", exception);
        }

        if (document.SchemaVersion != 1 || document.Channel is not ("alpha" or "development"))
        {
            throw new InvalidDataException("The Balls manifest has an unsupported schema or channel.");
        }
        if (!TagPattern().IsMatch(document.Release.Tag) || document.Release.Tag.Contains("..", StringComparison.Ordinal) ||
            !CommitPattern().IsMatch(document.Release.Commit) || !document.Release.Unsigned)
        {
            throw new InvalidDataException("The Balls manifest has an invalid release identity.");
        }

        var expectedReleaseUrl = $"https://github.com/scwlkr/balls/releases/tag/{document.Release.Tag}";
        if (!Uri.TryCreate(document.Release.Url, UriKind.Absolute, out var releaseUrl) ||
            !string.Equals(releaseUrl.AbsoluteUri, expectedReleaseUrl, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Balls manifest has an unexpected release URL.");
        }

        if (!document.Platforms.TryGetValue("windows-x64", out var deliveryElement))
        {
            throw new InvalidDataException("The Balls manifest does not contain a Windows package.");
        }
        DeliveryDocument delivery;
        try
        {
            delivery = deliveryElement.Deserialize<DeliveryDocument>(JsonOptions)
                ?? throw new InvalidDataException("The Balls Windows delivery is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Balls Windows delivery is not valid.", exception);
        }
        if (!string.Equals(delivery.Delivery, "package", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Balls manifest does not contain a Windows package.");
        }

        var identity = new PackageIdentity(
            delivery.Identity.Product,
            delivery.Identity.Version,
            delivery.Identity.Commit,
            delivery.Identity.Platform,
            delivery.Identity.Architecture);
        if (!string.Equals(identity.Product, "Balls", StringComparison.Ordinal) ||
            !VersionPattern().IsMatch(identity.Version) ||
            !string.Equals(identity.Commit, document.Release.Commit, StringComparison.Ordinal) ||
            !string.Equals(identity.Platform, "windows", StringComparison.Ordinal) ||
            !string.Equals(identity.Architecture, "x64", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Windows package identity does not match the release.");
        }

        var runtime = ValidateRuntime(delivery.Runtime);
        var versionPattern = Regex.Escape(identity.Version);
        var commitPrefix = Regex.Escape(identity.Commit[..12]);
        var archive = ValidateAsset(
            delivery.Archive,
            document.Release.Tag,
            new Regex($"^balls-{versionPattern}-canary-windows-x64-{commitPrefix}\\.zip$", RegexOptions.CultureInvariant));
        var checksum = ValidateAsset(
            delivery.Checksum,
            document.Release.Tag,
            new Regex($"^{Regex.Escape(archive.Name)}\\.sha256$", RegexOptions.CultureInvariant));
        var installer = ValidateAsset(
            delivery.Installer,
            document.Release.Tag,
            InstallerPattern());

        return new ValidatedRelease(
            document.Channel,
            document.Release.Tag,
            document.Release.Commit,
            identity,
            runtime,
            archive,
            checksum,
            installer);
    }

    public static void ValidateOfficialManifestUri(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            !string.Equals(uri.Host, "balls.wlkrlabs.com", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !ManifestPathPattern().IsMatch(uri.AbsolutePath))
        {
            throw new InvalidDataException(
                "Balls installs only from an official channel or immutable version manifest.");
        }
    }

    private static RuntimeContract ValidateRuntime(RuntimeDocument document)
    {
        if (!string.Equals(document.Architecture, "x64", StringComparison.Ordinal) ||
            document.Kind is not ("self-contained" or "framework-dependent"))
        {
            throw new InvalidDataException("The Balls manifest contains an unsupported Windows runtime contract.");
        }

        var frameworks = document.Frameworks ?? [];
        if (document.Kind == "self-contained" && frameworks.Count != 0)
        {
            throw new InvalidDataException("A self-contained Balls package cannot require external frameworks.");
        }
        if (document.Kind == "framework-dependent" && frameworks.Count == 0)
        {
            throw new InvalidDataException("The Balls manifest contains an empty Windows runtime contract.");
        }

        var result = new List<RuntimeFramework>(frameworks.Count);
        foreach (var framework in frameworks)
        {
            if (!FrameworkPattern().IsMatch(framework.Name) || framework.Major is < 1 or > 999)
            {
                throw new InvalidDataException("The Balls manifest contains an invalid Windows runtime framework.");
            }
            result.Add(new RuntimeFramework(framework.Name, framework.Major));
        }
        return new RuntimeContract(document.Kind, document.Architecture, result);
    }

    private static ReleaseAsset ValidateAsset(
        AssetDocument document,
        string tag,
        Regex namePattern)
    {
        if (!AssetNamePattern().IsMatch(document.Name) || document.Name.Contains("..", StringComparison.Ordinal) ||
            !namePattern.IsMatch(document.Name) ||
            !string.Equals(Path.GetFileName(document.Name), document.Name, StringComparison.Ordinal) ||
            !Sha256Pattern().IsMatch(document.Sha256))
        {
            throw new InvalidDataException("The Balls manifest contains an invalid Windows asset.");
        }
        if (!Uri.TryCreate(document.Url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            !string.Equals(uri.Host, "github.com", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !string.Equals(
                uri.AbsolutePath,
                $"/scwlkr/balls/releases/download/{tag}/{document.Name}",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Balls manifest contains an unexpected Windows asset URL.");
        }
        return new ReleaseAsset(document.Name, uri, document.Sha256);
    }

    private sealed class ReleaseManifestDocument
    {
        public int SchemaVersion { get; init; }
        public required string Channel { get; init; }
        public required ReleaseDocument Release { get; init; }
        public required Dictionary<string, JsonElement> Platforms { get; init; }
    }

    private sealed class ReleaseDocument
    {
        public required string Tag { get; init; }
        public required string Commit { get; init; }
        public required string PublishedAt { get; init; }
        public required string Url { get; init; }
        public bool Unsigned { get; init; }
    }

    private sealed class DeliveryDocument
    {
        public required string Delivery { get; init; }
        public required IdentityDocument Identity { get; init; }
        public required RuntimeDocument Runtime { get; init; }
        public required AssetDocument Archive { get; init; }
        public required AssetDocument Checksum { get; init; }
        public required AssetDocument Installer { get; init; }
    }

    private sealed class IdentityDocument
    {
        public required string Product { get; init; }
        public required string Version { get; init; }
        public required string Commit { get; init; }
        public required string Platform { get; init; }
        public required string Architecture { get; init; }
    }

    private sealed class RuntimeDocument
    {
        public required string Kind { get; init; }
        public required string Architecture { get; init; }
        public List<RuntimeFrameworkDocument>? Frameworks { get; init; }
    }

    private sealed class RuntimeFrameworkDocument
    {
        public required string Name { get; init; }
        public int Major { get; init; }
    }

    private sealed class AssetDocument
    {
        public required string Name { get; init; }
        public required string Url { get; init; }
        public required string Sha256 { get; init; }
    }

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z._-]{0,191}$", RegexOptions.CultureInvariant)]
    private static partial Regex AssetNamePattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9.]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex FrameworkPattern();

    [GeneratedRegex("^Install-BallsCanary\\.ps1$", RegexOptions.CultureInvariant)]
    private static partial Regex InstallerPattern();

    [GeneratedRegex("^/(channels/(alpha|development)|versions/[0-9A-Za-z][0-9A-Za-z._-]{0,127})\\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex ManifestPathPattern();
}
