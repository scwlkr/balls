using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Balls.WindowsConformance;

internal sealed record WindowsConformanceTargetProfile(
    string Schema,
    string Operation,
    bool Authorized,
    string TargetId,
    string ExpectedComputerName,
    string ExpectedAccountKind,
    string ExpectedProductAccountSidSha256,
    string ConnectivityPath,
    WindowsConformanceSshTransport Transport,
    WindowsConformanceSshTransport ProductTransport,
    string? DisposablePath = null,
    string? ExpectedVolumeIdentitySha256 = null,
    string? ExpectedDiskIdentitySha256 = null);

internal sealed record WindowsConformanceSshTransport(
    string Host,
    int Port,
    string User,
    string KnownHostsFile,
    string PublicKeyFile);

internal sealed class ConformanceRefusalException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

internal static partial class WindowsConformanceTargetProfileLoader
{
    private const int MaximumProfileBytes = 16 * 1024;

    public static WindowsConformanceTargetProfile Load(string path)
    {
        var profilePath = Path.GetFullPath(path);
        var file = new FileInfo(profilePath);
        if (!file.Exists || file.Length is <= 0 or > MaximumProfileBytes)
        {
            throw new ConformanceRefusalException("target_profile_invalid");
        }

        WindowsConformanceTargetProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<WindowsConformanceTargetProfile>(
                File.ReadAllText(profilePath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    RespectNullableAnnotations = true,
                    RespectRequiredConstructorParameters = true,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                });
        }
        catch (JsonException)
        {
            throw new ConformanceRefusalException("target_profile_invalid");
        }

        if (profile is null || profile.Schema != "balls-windows-conformance-target-v1")
        {
            throw new ConformanceRefusalException("target_profile_invalid");
        }

        if (profile.Operation is not ("windows-smb-readiness-v1" or "windows-circle-files-host-v1"))
        {
            throw new ConformanceRefusalException("operation_not_allowed");
        }

        if (!profile.Authorized)
        {
            throw new ConformanceRefusalException("target_not_authorized");
        }

        if (!TargetIdPattern().IsMatch(profile.TargetId)
            || !ComputerNamePattern().IsMatch(profile.ExpectedComputerName)
            || profile.ExpectedAccountKind is not ("administrator" or "standard")
            || !Sha256Pattern().IsMatch(profile.ExpectedProductAccountSidSha256)
            || string.IsNullOrWhiteSpace(profile.ConnectivityPath)
            || profile.ConnectivityPath.Length > 160
            || profile.ConnectivityPath.Any(character => char.IsControl(character)))
        {
            throw new ConformanceRefusalException("target_profile_invalid");
        }

        if (profile.Operation == "windows-smb-readiness-v1"
            && (profile.DisposablePath is not null
                || profile.ExpectedVolumeIdentitySha256 is not null
                || profile.ExpectedDiskIdentitySha256 is not null))
        {
            throw new ConformanceRefusalException("target_profile_invalid");
        }

        if (profile.Operation == "windows-circle-files-host-v1"
            && (profile.ExpectedAccountKind != "administrator"
                || !DisposableHostPathPattern().IsMatch(profile.DisposablePath ?? string.Empty)))
        {
            throw new ConformanceRefusalException("disposable_path_not_authorized");
        }

        if (profile.Operation == "windows-circle-files-host-v1"
            && (!Sha256Pattern().IsMatch(profile.ExpectedVolumeIdentitySha256 ?? string.Empty)
                || !Sha256Pattern().IsMatch(profile.ExpectedDiskIdentitySha256 ?? string.Empty)))
        {
            throw new ConformanceRefusalException("disposable_storage_not_authorized");
        }

        var profileDirectory = Path.GetDirectoryName(profilePath)!;
        var transport = ValidateTransport(profileDirectory, profile.Transport);
        var productTransport = ValidateTransport(profileDirectory, profile.ProductTransport);
        if (!string.Equals(transport.Host, productTransport.Host, StringComparison.OrdinalIgnoreCase)
            || transport.Port != productTransport.Port
            || !string.Equals(
                transport.KnownHostsFile,
                productTransport.KnownHostsFile,
                StringComparison.Ordinal))
        {
            throw new ConformanceRefusalException("transport_target_mismatch");
        }

        return profile with
        {
            Transport = transport,
            ProductTransport = productTransport,
        };
    }

    private static WindowsConformanceSshTransport ValidateTransport(
        string profileDirectory,
        WindowsConformanceSshTransport transport)
    {
        if (!IPAddress.TryParse(transport.Host, out var address)
            || !IPAddress.IsLoopback(address))
        {
            throw new ConformanceRefusalException("transport_not_loopback");
        }

        if (transport.Port is < 1 or > 65535
            || !SshUserPattern().IsMatch(transport.User))
        {
            throw new ConformanceRefusalException("target_profile_invalid");
        }

        return transport with
        {
            KnownHostsFile = ResolveFile(profileDirectory, transport.KnownHostsFile),
            PublicKeyFile = ResolveFile(profileDirectory, transport.PublicKeyFile),
        };
    }

    private static string ResolveFile(string profileDirectory, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ConformanceRefusalException("target_profile_invalid");
        }

        var path = Path.GetFullPath(value, profileDirectory);
        if (!File.Exists(path))
        {
            throw new ConformanceRefusalException("target_profile_invalid");
        }

        return path;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex TargetIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex ComputerNamePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SshUserPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex(
        "^[C-Z]:\\\\BallsConformance\\\\Issue124-[A-Za-z0-9][A-Za-z0-9-]{2,39}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DisposableHostPathPattern();
}
