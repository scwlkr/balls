namespace Balls.Canary;

internal sealed class CanaryUsageException(string message) : Exception(message);

internal static class CanaryCommandParser
{
    public static CanaryPackageRequest Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 3 || arguments[0] != "package" || arguments.Count % 2 == 0)
        {
            throw Usage();
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < arguments.Count; index += 2)
        {
            var name = arguments[index];
            if (!KnownOptions.Contains(name) || !values.TryAdd(name, arguments[index + 1]))
            {
                throw Usage();
            }
        }

        if (!KnownOptions.All(values.ContainsKey))
        {
            throw Usage();
        }

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

    private static CanaryUsageException Usage() => new(
        "Usage: Balls.Canary package --repository-root <path> --cli-directory <path> " +
        "--daemon-directory <path> --output-directory <path> --platform <windows|linux> " +
        "--architecture <name> --commit <full-sha>");
}
