using System.Net;
using System.Net.Sockets;

namespace Balls.Bootstrap.Windows;

internal sealed record BootstrapOptions(
    Uri? ManifestUri,
    string? PackagePath,
    string? ChecksumPath,
    string InstallRoot,
    string PipeName,
    string NodeName,
    bool OpenUi,
    bool CreateShortcut,
    string? AdvertisedPrivateAddress = null)
{
    public bool IsManifestInstall => ManifestUri is not null;
}

internal static class BootstrapOptionsParser
{
    public const string Usage =
        "Usage: balls-bootstrap-windows-x64 --manifest-uri <official-uri> " +
        "[--install-root <path>]\n" +
        "   or: balls-bootstrap-windows-x64 --package-path <zip> --checksum-path <sha256> " +
        "--install-root <path> [--pipe-name <name>] [--node-name <name>] " +
        "[--open-ui <true|false>] [--create-shortcut <true|false>] " +
        "[--advertised-private-address <private-ip>]";

    public static BootstrapOptions Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2 || arguments.Count % 2 != 0)
        {
            throw new ArgumentException(Usage);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index += 2)
        {
            var name = arguments[index];
            if (!KnownOptions.Contains(name) || !values.TryAdd(name, arguments[index + 1]))
            {
                throw new ArgumentException(Usage);
            }
        }

        var hasManifest = values.TryGetValue("--manifest-uri", out var manifestText);
        var hasPackage = values.TryGetValue("--package-path", out var packagePath);
        var hasChecksum = values.TryGetValue("--checksum-path", out var checksumPath);
        if (hasManifest == hasPackage || hasPackage != hasChecksum)
        {
            throw new ArgumentException(Usage);
        }

        Uri? manifestUri = null;
        if (hasManifest && (!Uri.TryCreate(manifestText, UriKind.Absolute, out manifestUri) || manifestUri is null))
        {
            throw new ArgumentException(Usage);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installRoot = values.GetValueOrDefault(
            "--install-root",
            Path.Combine(localAppData, hasManifest ? "Balls" : "Balls-Canary"));
        var pipeName = values.GetValueOrDefault("--pipe-name", "balls");
        var nodeName = values.GetValueOrDefault("--node-name", Environment.MachineName);
        var advertisedPrivateAddress = values.GetValueOrDefault("--advertised-private-address");
        if (string.IsNullOrWhiteSpace(installRoot) || string.IsNullOrWhiteSpace(pipeName) ||
            pipeName.Length > 100 || pipeName.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-') ||
            string.IsNullOrWhiteSpace(nodeName) || nodeName.Length > 100 || nodeName.Contains('"') ||
            (advertisedPrivateAddress is not null &&
                (!IPAddress.TryParse(advertisedPrivateAddress, out var parsedAdvertisedAddress) ||
                 !IsPrivateIPv4(parsedAdvertisedAddress))))
        {
            throw new ArgumentException(Usage);
        }

        return new BootstrapOptions(
            manifestUri,
            packagePath,
            checksumPath,
            Path.GetFullPath(installRoot),
            pipeName,
            nodeName,
            ParseBoolean(values, "--open-ui", defaultValue: true),
            ParseBoolean(values, "--create-shortcut", defaultValue: hasManifest),
            advertisedPrivateAddress);
    }

    private static bool ParseBoolean(
        IReadOnlyDictionary<string, string> values,
        string name,
        bool defaultValue)
    {
        if (!values.TryGetValue(name, out var text))
        {
            return defaultValue;
        }
        return text switch
        {
            "true" => true,
            "false" => false,
            _ => throw new ArgumentException(Usage),
        };
    }

    private static bool IsPrivateIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
        {
            return false;
        }
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }

    private static readonly HashSet<string> KnownOptions = new(StringComparer.Ordinal)
    {
        "--manifest-uri",
        "--package-path",
        "--checksum-path",
        "--install-root",
        "--pipe-name",
        "--node-name",
        "--open-ui",
        "--create-shortcut",
        "--advertised-private-address",
    };
}
