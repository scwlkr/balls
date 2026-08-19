using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;

namespace Balls.Verify;

internal enum VerificationMode
{
    Focused,
    Fast,
    Full,
}

internal enum TestCountRule
{
    None,
    RequireZero,
    RequireAtLeastOne,
}

internal sealed record VerificationRequest(
    VerificationMode Mode,
    string? Project = null,
    string? Filter = null,
    string? WebScript = null);

internal sealed record VerificationCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null);

internal sealed record VerificationStep(
    VerificationCommand Command,
    TestCountRule TestCountRule = TestCountRule.None,
    string? ResultsDirectory = null);

internal sealed record VerificationPlan(IReadOnlyList<VerificationStep> Steps);

internal sealed class UsageException(string message) : Exception(message);

internal interface ICommandRunner
{
    Task<int> RunAsync(VerificationCommand command, CancellationToken cancellationToken);
}

internal static class VerificationExitCodes
{
    public const int Success = 0;
    public const int Usage = 2;
    public const int NoTestsSelected = 3;
    public const int UnclassifiedTests = 4;
}

internal static class VerificationRequestParser
{
    private static readonly HashSet<string> WebScripts =
    [
        "build",
        "e2e",
        "format:check",
        "generate:check",
        "lint",
        "test",
        "typecheck",
    ];

    public static VerificationRequest Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 1 && arguments[0] is "fast" or "full")
        {
            return new VerificationRequest(
                arguments[0] == "fast" ? VerificationMode.Fast : VerificationMode.Full);
        }

        if (arguments.Count == 5 && arguments[0] == "focused")
        {
            string? project = null;
            string? filter = null;
            for (var index = 1; index < arguments.Count; index += 2)
            {
                switch (arguments[index])
                {
                    case "--project":
                        project = arguments[index + 1];
                        break;
                    case "--filter":
                        filter = arguments[index + 1];
                        break;
                    default:
                        throw Usage();
                }
            }

            if (!string.IsNullOrWhiteSpace(project) && !string.IsNullOrWhiteSpace(filter))
            {
                return new VerificationRequest(VerificationMode.Focused, project, filter);
            }
        }

        if (arguments.Count == 3
            && arguments[0] == "focused"
            && arguments[1] == "--web"
            && WebScripts.Contains(arguments[2]))
        {
            return new VerificationRequest(
                VerificationMode.Focused,
                WebScript: arguments[2]);
        }

        throw Usage();
    }

    private static UsageException Usage() => new(
        "Usage: Balls.Verify focused --project <path> --filter <expression> | " +
        "focused --web <build|e2e|format:check|generate:check|lint|test|typecheck> | fast | full");
}

internal static class VerificationPlanner
{
    internal const string FastFilter =
        "(TestCategory=Unit|TestCategory=Contract|TestCategory=ProcessIntegration)";

    internal const string UncategorizedFilter =
        "TestCategory!=Unit&TestCategory!=Contract&TestCategory!=ProcessIntegration&" +
        "TestCategory!=OSIntegration&TestCategory!=Browser&TestCategory!=Lab";

    public static VerificationPlan Create(
        VerificationRequest request,
        string repositoryRoot,
        string resultsDirectory)
    {
        if (request.Mode == VerificationMode.Focused)
        {
            if (request.WebScript is not null)
            {
                return new VerificationPlan(
                    [
                        new VerificationStep(Pnpm(repositoryRoot, "install", "--frozen-lockfile")),
                        new VerificationStep(Pnpm(repositoryRoot, $"web:{request.WebScript}")),
                    ]);
            }

            return new VerificationPlan(
                [new VerificationStep(
                    Dotnet(
                        repositoryRoot,
                        "test",
                        request.Project!,
                        "--configuration",
                        "Release",
                        "--filter",
                        request.Filter!,
                        "--logger",
                        "trx;LogFileName=focused.trx",
                        "--results-directory",
                        resultsDirectory),
                    TestCountRule.RequireAtLeastOne,
                    resultsDirectory)]);
        }

        var solution = Path.Combine(repositoryRoot, "Balls.slnx");
        var finalArguments = new List<string>
        {
            "test",
            solution,
            "--configuration",
            "Release",
            "--no-build",
            "--no-restore",
        };
        if (request.Mode == VerificationMode.Fast)
        {
            finalArguments.Add("--filter");
            finalArguments.Add(FastFilter);
        }

        return new VerificationPlan(
            [
                new VerificationStep(Dotnet(repositoryRoot, "restore", solution, "--locked-mode")),
                new VerificationStep(Pnpm(repositoryRoot, "install", "--frozen-lockfile")),
                new VerificationStep(Dotnet(
                    repositoryRoot,
                    "format",
                    solution,
                    "--verify-no-changes",
                    "--no-restore")),
                new VerificationStep(Pnpm(repositoryRoot, "web:generate:check")),
                new VerificationStep(Pnpm(repositoryRoot, "web:format:check")),
                new VerificationStep(Dotnet(
                    repositoryRoot,
                    "build",
                    solution,
                    "--configuration",
                    "Release",
                    "--no-restore")),
                new VerificationStep(Pnpm(repositoryRoot, "web:lint")),
                new VerificationStep(Pnpm(repositoryRoot, "web:typecheck")),
                new VerificationStep(
                    Dotnet(
                        repositoryRoot,
                        "test",
                        solution,
                        "--configuration",
                        "Release",
                        "--no-build",
                        "--no-restore",
                        "--filter",
                        UncategorizedFilter,
                        "--logger",
                        "trx;LogFilePrefix=category-audit",
                        "--results-directory",
                        resultsDirectory),
                    TestCountRule.RequireZero,
                    resultsDirectory),
                new VerificationStep(new VerificationCommand("dotnet", finalArguments, repositoryRoot)),
                new VerificationStep(Pnpm(repositoryRoot, "web:test")),
                new VerificationStep(Pnpm(repositoryRoot, "web:build")),
                new VerificationStep(Pnpm(repositoryRoot, "web:e2e")),
            ]);
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

internal static class TrxSummary
{
    public static int ReadTotal(string resultsDirectory)
    {
        var files = Directory.GetFiles(resultsDirectory, "*.trx", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            throw new InvalidOperationException("The test command did not produce a TRX result.");
        }

        var total = 0;
        foreach (var file in files)
        {
            var counters = XDocument.Load(file)
                .Descendants()
                .SingleOrDefault(element => element.Name.LocalName == "Counters");
            var rawTotal = counters?.Attribute("total")?.Value;
            if (!int.TryParse(rawTotal, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                throw new InvalidOperationException($"The TRX result '{file}' has no total count.");
            }

            total += count;
        }

        return total;
    }
}

internal sealed class VerificationEngine(ICommandRunner commandRunner)
{
    public async Task<int> ExecuteAsync(
        VerificationPlan plan,
        CancellationToken cancellationToken = default)
    {
        foreach (var step in plan.Steps)
        {
            if (step.TestCountRule != TestCountRule.None && step.ResultsDirectory is not null)
            {
                if (Directory.Exists(step.ResultsDirectory))
                {
                    Directory.Delete(step.ResultsDirectory, recursive: true);
                }

                Directory.CreateDirectory(step.ResultsDirectory);
            }

            var exitCode = await commandRunner.RunAsync(step.Command, cancellationToken);
            if (exitCode != VerificationExitCodes.Success)
            {
                return exitCode;
            }

            if (step.TestCountRule == TestCountRule.None)
            {
                continue;
            }

            var total = TrxSummary.ReadTotal(step.ResultsDirectory!);
            if (step.TestCountRule == TestCountRule.RequireAtLeastOne && total == 0)
            {
                Console.Error.WriteLine("Focused verification selected zero tests.");
                return VerificationExitCodes.NoTestsSelected;
            }

            if (step.TestCountRule == TestCountRule.RequireZero && total != 0)
            {
                Console.Error.WriteLine($"Category audit found {total} unclassified test(s).");
                return VerificationExitCodes.UnclassifiedTests;
            }
        }

        return VerificationExitCodes.Success;
    }
}

internal sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<int> RunAsync(
        VerificationCommand command,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"> {command.FileName} {string.Join(' ', command.Arguments.Select(Quote))}");
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = command.WorkingDirectory ?? Environment.CurrentDirectory,
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
