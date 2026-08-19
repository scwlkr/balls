using System.Reflection;
using Balls.Core;
using Balls.Platform.Windows;

namespace Balls.Daemon;

public static class DaemonExitCodes
{
    public const int Success = 0;
    public const int UsageError = 2;
    public const int StartupFailure = 4;
    public const int PlatformUnsupported = 5;
}

public static class DaemonCommand
{
    public static async Task<int> RunAsync(
        string[] arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (arguments.SequenceEqual(["--version"], StringComparer.Ordinal))
        {
            await standardOutput.WriteLineAsync(GetProductVersion());
            return DaemonExitCodes.Success;
        }

        if (arguments.SequenceEqual(["--help"], StringComparer.Ordinal))
        {
            await WriteUsageAsync(standardOutput);
            return DaemonExitCodes.Success;
        }

        if (!OperatingSystem.IsWindows())
        {
            await standardError.WriteLineAsync(
                "ballsd: the Phase 1 local control transport currently requires Windows.");
            return DaemonExitCodes.PlatformUnsupported;
        }

        var tokens = arguments.ToList();
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Balls");
        var pipeName = WindowsNamedPipeDefaults.GetCurrentUserPipeName();
        var nodeName = Environment.MachineName;

        if (!TryApplyOption(tokens, "--data-directory", ref dataDirectory, out var error)
            || !TryApplyOption(tokens, "--pipe-name", ref pipeName, out error)
            || !TryApplyOption(tokens, "--node-name", ref nodeName, out error))
        {
            await standardError.WriteLineAsync($"ballsd: {error}");
            await WriteUsageAsync(standardError);
            return DaemonExitCodes.UsageError;
        }

        if (tokens.Count != 0)
        {
            await standardError.WriteLineAsync($"ballsd: unknown argument '{tokens[0]}'.");
            await WriteUsageAsync(standardError);
            return DaemonExitCodes.UsageError;
        }

        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            await standardError.WriteLineAsync("ballsd: --data-directory requires a non-blank value.");
            await WriteUsageAsync(standardError);
            return DaemonExitCodes.UsageError;
        }

        if (string.IsNullOrWhiteSpace(nodeName))
        {
            await standardError.WriteLineAsync("ballsd: --node-name requires a non-blank value.");
            await WriteUsageAsync(standardError);
            return DaemonExitCodes.UsageError;
        }
        if (nodeName.Trim().Length > 100)
        {
            await standardError.WriteLineAsync("ballsd: --node-name cannot exceed 100 characters.");
            await WriteUsageAsync(standardError);
            return DaemonExitCodes.UsageError;
        }

        try
        {
            WindowsNamedPipeControl.ValidatePipeName(pipeName);
        }
        catch (ArgumentException)
        {
            await standardError.WriteLineAsync("ballsd: invalid --pipe-name value.");
            await WriteUsageAsync(standardError);
            return DaemonExitCodes.UsageError;
        }

        try
        {
            await using var daemon = await DaemonHost.StartAsync(
                new DaemonOptions(dataDirectory, pipeName, nodeName),
                cancellationToken).ConfigureAwait(false);
            await standardOutput.WriteLineAsync($"ballsd ready on named pipe {pipeName}.");
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            return DaemonExitCodes.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DaemonExitCodes.Success;
        }
        catch (Exception exception) when (
            exception is DataDirectoryInUseException
                or LocalStateException
                or InputValidationException
                or UnauthorizedAccessException
                or IOException)
        {
            await standardError.WriteLineAsync($"ballsd: {exception.Message}");
            return DaemonExitCodes.StartupFailure;
        }
    }

    private static bool TryApplyOption(
        List<string> tokens,
        string option,
        ref string destination,
        out string? error)
    {
        var index = tokens.FindIndex(token => string.Equals(token, option, StringComparison.Ordinal));
        if (index < 0)
        {
            error = null;
            return true;
        }

        if (index == tokens.Count - 1)
        {
            error = $"{option} requires a value.";
            return false;
        }

        destination = tokens[index + 1];
        tokens.RemoveRange(index, 2);
        error = null;
        return true;
    }

    private static Task WriteUsageAsync(TextWriter writer)
    {
        return writer.WriteLineAsync(
            "usage: ballsd [--data-directory <path>] [--pipe-name <name>] [--node-name <name>]");
    }

    private static string GetProductVersion()
    {
        return typeof(DaemonCommand).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+', 2)[0]
            ?? "unknown";
    }
}
