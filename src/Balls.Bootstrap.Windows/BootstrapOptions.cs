namespace Balls.Bootstrap.Windows;

internal sealed record BootstrapOptions(
    Uri? ManifestUri,
    string? PackagePath,
    string? ChecksumPath,
    string InstallRoot,
    string PipeName,
    string NodeName,
    bool OpenUi,
    bool CreateShortcut)
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
        "[--open-ui <true|false>] [--create-shortcut <true|false>]";

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
        if (string.IsNullOrWhiteSpace(installRoot) || string.IsNullOrWhiteSpace(pipeName) ||
            pipeName.Length > 100 || pipeName.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-') ||
            string.IsNullOrWhiteSpace(nodeName) || nodeName.Length > 100 || nodeName.Contains('"'))
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
            ParseBoolean(values, "--create-shortcut", defaultValue: hasManifest));
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
    };
}
