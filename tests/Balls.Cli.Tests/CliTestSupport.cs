using System.Text.Json;
using Balls.Cli;
using Balls.Protocol.Control.V1;

namespace Balls.Cli.Tests;

internal static class CliTestSupport
{
    internal static async Task<CliResult> RunAsync(
        string pipeName,
        params string[] command)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var arguments = new[] { "--pipe-name", pipeName }.Concat(command).ToArray();
        var exitCode = await CliApplication.RunAsync(arguments, output, error);
        return new CliResult(exitCode, output.ToString().Trim(), error.ToString().Trim());
    }

    internal static T DeserializeResult<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(1, document.RootElement.GetProperty("outputVersion").GetInt32());
        return document.RootElement.GetProperty("result").Deserialize<T>(ControlJson.Options)
            ?? throw new AssertFailedException("CLI result was null.");
    }
}

internal sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed class TemporaryDirectory : IDisposable
{
    internal TemporaryDirectory()
    {
        Path = OperatingSystem.IsMacOS()
            ? System.IO.Path.Combine(
                GetCanonicalTempPath(),
                $"bt-{Guid.NewGuid():N}"[..11])
            : System.IO.Path.Combine(
                OperatingSystem.IsLinux()
                    ? System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".local",
                        "state")
                    : System.IO.Path.GetTempPath(),
                "balls-tests",
                Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    private static string GetCanonicalTempPath()
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        return path.StartsWith("/var/", StringComparison.Ordinal)
            ? "/private" + path
            : path;
    }
}
