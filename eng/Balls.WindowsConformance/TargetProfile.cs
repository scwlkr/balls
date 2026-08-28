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
    WindowsConformanceSshTransport ProductTransport);

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

        if (profile.Operation != "windows-smb-readiness-v1")
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

        var profileDirectory = Path.GetDirectoryName(profilePath)!;
        return profile with
        {
            Transport = ValidateTransport(profileDirectory, profile.Transport),
            ProductTransport = ValidateTransport(profileDirectory, profile.ProductTransport),
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
}
