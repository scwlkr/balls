using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Balls.Platform;

namespace Balls.Daemon;

internal sealed partial class FileRevitServerPackageIdentitySource(string path)
    : IRevitServerPackageIdentitySource
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public RevitServerPackageIdentity Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > 32 * 1024)
            {
                throw new InvalidDataException("The Balls installation identity is unavailable.");
            }
            var document = JsonSerializer.Deserialize<InstallationDocument>(File.ReadAllBytes(path), Options)
                ?? throw new InvalidDataException("The Balls installation identity is empty.");
            if (document.SchemaVersion != 1
                || document.Product != "Balls"
                || document.Channel != "development"
                || !DevelopmentTagPattern().IsMatch(document.Release.Tag)
                || !CommitPattern().IsMatch(document.Release.Commit)
                || !document.Release.Tag.EndsWith(document.Release.Commit[..12], StringComparison.Ordinal)
                || !Sha256Pattern().IsMatch(document.Package.Sha256)
                || document.Package.Platform != "windows"
                || document.Package.Architecture != "x64"
                || string.IsNullOrWhiteSpace(document.Package.Name)
                || string.IsNullOrWhiteSpace(document.Package.Version))
            {
                throw new InvalidDataException("The Balls installation identity is not an exact Development Windows package.");
            }
            return new RevitServerPackageIdentity(
                document.Release.Tag,
                document.Release.Commit,
                document.Package.Name,
                document.Package.Version,
                document.Package.Sha256);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("The Balls installation identity could not be verified.", exception);
        }
    }

    private sealed record InstallationDocument(
        int SchemaVersion,
        string Product,
        string Channel,
        string? ManifestUri,
        DateTimeOffset InstalledAt,
        ReleaseIdentity Release,
        PackageIdentity Package);
    private sealed record ReleaseIdentity(string Tag, string Commit);
    private sealed record PackageIdentity(
        string Name,
        string Sha256,
        string Version,
        string Platform,
        string Architecture);

    [GeneratedRegex("^development-[0-9]{8}T[0-9]{6}Z-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex DevelopmentTagPattern();
    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();
    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}

internal sealed class UnsupportedRevitServerPackageIdentitySource : IRevitServerPackageIdentitySource
{
    public RevitServerPackageIdentity Load() =>
        throw new InvalidDataException("Install an exact Balls Development package through balls.wlkrlabs.com before exporting the handoff.");
}
