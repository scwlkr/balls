namespace Balls.Canary;

internal sealed class CanaryUsageException(string message) : Exception(message);

internal static class CanaryCommandParser
{
    public static CanaryPackageRequest Parse(IReadOnlyList<string> arguments)
    {
        var values = CanaryOptionParser.Parse(arguments, "package", KnownOptions, UsageMessage);

        var platform = values["--platform"] switch
        {
            "windows" => CanaryPlatform.Windows,
            "linux" => CanaryPlatform.Linux,
            _ => throw Usage(),
        };

        return new CanaryPackageRequest(
            values["--repository-root"],
            values["--cli-directory"],
            values["--daemon-directory"],
            values["--output-directory"],
            platform,
            values["--architecture"],
            values["--commit"]);
    }

    private static readonly string[] KnownOptions =
    [
        "--repository-root",
        "--cli-directory",
        "--daemon-directory",
        "--output-directory",
        "--platform",
        "--architecture",
        "--commit",
    ];

    private const string UsageMessage =
        "Usage: Balls.Canary package --repository-root <path> --cli-directory <path> " +
        "--daemon-directory <path> --output-directory <path> --platform <windows|linux> " +
        "--architecture <name> --commit <full-sha>";

    private static CanaryUsageException Usage() => new(UsageMessage);
}

internal sealed record DevelopmentManifestRequest(
    string PublicRoot,
    string PackagePath,
    string ChecksumPath,
    string InstallerPath,
    string BootstrapPath,
    string Tag,
    string Commit,
    string PublishedAt);

internal static class DevelopmentManifestCommandParser
{
    public static DevelopmentManifestRequest Parse(IReadOnlyList<string> arguments)
    {
        var values = CanaryOptionParser.Parse(
            arguments,
            "development-manifest",
            KnownOptions,
            UsageMessage);

        return new DevelopmentManifestRequest(
            values["--public-root"],
            values["--package-path"],
            values["--checksum-path"],
            values["--installer-path"],
            values["--bootstrap-path"],
            values["--tag"],
            values["--commit"],
            values["--published-at"]);
    }

    private static readonly string[] KnownOptions =
    [
        "--public-root",
        "--package-path",
        "--checksum-path",
        "--installer-path",
        "--bootstrap-path",
        "--tag",
        "--commit",
        "--published-at",
    ];

    private const string UsageMessage =
        "Usage: Balls.Canary development-manifest --public-root <path> --package-path <zip> " +
        "--checksum-path <sha256> --installer-path <ps1> --bootstrap-path <exe> --tag <development-tag> " +
        "--commit <full-sha> --published-at <utc-timestamp>";
}

internal static class CanaryOptionParser
{
    internal static IReadOnlyDictionary<string, string> Parse(
        IReadOnlyList<string> arguments,
        string command,
        IReadOnlyCollection<string> knownOptions,
        string usageMessage)
    {
        if (arguments.Count < 3
            || !string.Equals(arguments[0], command, StringComparison.Ordinal)
            || arguments.Count % 2 == 0)
        {
            throw new CanaryUsageException(usageMessage);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < arguments.Count; index += 2)
        {
            var name = arguments[index];
            if (!knownOptions.Contains(name) || !values.TryAdd(name, arguments[index + 1]))
            {
                throw new CanaryUsageException(usageMessage);
            }
        }

        if (!knownOptions.All(values.ContainsKey))
        {
            throw new CanaryUsageException(usageMessage);
        }

        return values;
    }
}
