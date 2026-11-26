using System.Diagnostics;

namespace Balls.Verify;

internal sealed record VerificationCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

internal static class VerificationPlanner
{
    private static readonly HashSet<string> WebScripts =
    [
        "build",
        "format:check",
        "generate:check",
        "lint",
        "typecheck",
    ];

    public static IReadOnlyList<VerificationCommand> Create(string[] arguments, string repositoryRoot)
    {
        if (arguments is ["focused", "--web", var script] && WebScripts.Contains(script))
        {
            return [Pnpm(repositoryRoot, "install", "--frozen-lockfile"), Pnpm(repositoryRoot, $"web:{script}")];
        }

        if (arguments is not ["fast"] and not ["full"])
        {
            throw new ArgumentException(
                "Usage: Balls.Verify focused --web <build|format:check|generate:check|lint|typecheck> | fast | full");
        }

        var solution = Path.Combine(repositoryRoot, "Balls.slnx");
        return
        [
            Dotnet(repositoryRoot, "restore", solution, "--locked-mode"),
            Pnpm(repositoryRoot, "install", "--frozen-lockfile"),
            Dotnet(
                repositoryRoot,
                "format",
                solution,
                "--verify-no-changes",
                "--no-restore"),
            Pnpm(repositoryRoot, "web:generate:check"),
            Pnpm(repositoryRoot, "web:format:check"),
            Pnpm(repositoryRoot, "downloads:format:check"),
            Dotnet(
                repositoryRoot,
                "build",
                solution,
                "--configuration",
                "Release",
                "--no-restore"),
            Pnpm(repositoryRoot, "web:lint"),
            Pnpm(repositoryRoot, "downloads:lint"),
            Pnpm(repositoryRoot, "web:typecheck"),
            Pnpm(repositoryRoot, "downloads:typecheck"),
            Pnpm(repositoryRoot, "web:build"),
            Pnpm(repositoryRoot, "downloads:build"),
        ];
    }

    private static VerificationCommand Dotnet(
        string workingDirectory,
        params string[] arguments) =>
        new("dotnet", arguments, workingDirectory);

    private static VerificationCommand Pnpm(
        string workingDirectory,
        params string[] arguments)
    {
        return OperatingSystem.IsWindows()
            ? new VerificationCommand(
                "cmd.exe",
                new[] { "/d", "/c", "pnpm" }.Concat(arguments).ToArray(),
                workingDirectory)
            : new VerificationCommand("pnpm", arguments, workingDirectory);
    }
}

internal sealed class ProcessCommandRunner
{
    public async Task<int> RunAsync(
        VerificationCommand command,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"> {command.FileName} {string.Join(' ', command.Arguments.Select(Quote))}");
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = command.WorkingDirectory,
        };
        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException($"Could not start '{command.FileName}'.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private static string Quote(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
}
