using System.Text.RegularExpressions;

namespace Balls.WindowsConformance;

internal static partial class ConformanceCommand
{
    public const int Success = 0;
    public const int UnexpectedFailure = 1;
    public const int UsageError = 2;
    public const int Refused = 3;
    public const int Cancelled = 4;

    public static async Task<int> RunAsync(
        string[] arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = Parse(arguments);
            var target = WindowsConformanceTargetProfileLoader.Load(request.TargetProfile);
            var package = WindowsPackageIdentityLoader.Load(
                request.Package,
                request.Checksum,
                request.ExpectedCommit);
            var repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
            var guestScriptPath = Path.Combine(
                repositoryRoot,
                "eng",
                "conformance",
                request.Operation.GuestScriptFileName);
            var guestScript = ReadGuestScript(guestScriptPath);
            if (!OperatingSystem.IsLinux())
            {
                throw new ConformanceRefusalException("linux_required");
            }

            if (target.Operation != request.Operation.ProfileOperation)
            {
                throw new ConformanceRefusalException("operation_profile_mismatch");
            }
            var outcome = await request.Operation.RunAsync(
                    new SystemConformanceProcessRunner(),
                    guestScript,
                    target,
                    package,
                    request.Receipt,
                    cancellationToken)
                .ConfigureAwait(false);
            await standardOutput.WriteLineAsync($"Outcome: {outcome}");
            await standardOutput.WriteLineAsync($"Receipt: {Path.GetFullPath(request.Receipt)}");
            return Success;
        }
        catch (ConformanceUsageException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            return UsageError;
        }
        catch (ConformanceRefusalException exception)
        {
            await standardError.WriteLineAsync($"windows-conformance: {exception.Code}");
            return Refused;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await standardError.WriteLineAsync("windows-conformance: cancelled");
            return Cancelled;
        }
        catch (Exception)
        {
            await standardError.WriteLineAsync("windows-conformance: unexpected_failure");
            return UnexpectedFailure;
        }
    }

    private static ConformanceRequest Parse(IReadOnlyList<string> arguments)
    {
        const string usage =
            "Usage: Balls.WindowsConformance <run|host-run> --target-profile <json> --package <zip> " +
            "--checksum <sha256> --expected-commit <full-sha> --receipt <json>";
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "--target-profile",
            "--package",
            "--checksum",
            "--expected-commit",
            "--receipt",
        };
        var operation = arguments.Count == 11
            ? ConformanceOperationStrategy.Resolve(arguments[0])
            : null;
        if (operation is null)
        {
            throw new ConformanceUsageException(usage);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < arguments.Count; index += 2)
        {
            if (!known.Contains(arguments[index])
                || !values.TryAdd(arguments[index], arguments[index + 1])
                || string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                throw new ConformanceUsageException(usage);
            }
        }

        if (values.Count != known.Count
            || !CommitPattern().IsMatch(values["--expected-commit"]))
        {
            throw new ConformanceUsageException(usage);
        }

        return new ConformanceRequest(
            operation,
            values["--target-profile"],
            values["--package"],
            values["--checksum"],
            values["--expected-commit"],
            values["--receipt"]);
    }

    private static string ReadGuestScript(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists
            || file.Length is <= 0 or > 128 * 1024
            || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ConformanceRefusalException("guest_operation_invalid");
        }

        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Balls.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new ConformanceRefusalException("repository_root_not_found");
    }

    [GeneratedRegex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();
}

internal sealed record ConformanceRequest(
    ConformanceOperationDescriptor Operation,
    string TargetProfile,
    string Package,
    string Checksum,
    string ExpectedCommit,
    string Receipt);

internal sealed class ConformanceUsageException(string message) : Exception(message);

internal sealed record ConformanceOperationDescriptor(
    string CommandName,
    string ProfileOperation,
    string GuestScriptFileName,
    Func<IConformanceProcessRunner, string, WindowsConformanceTargetProfile, WindowsPackageIdentity,
        string, CancellationToken, Task<string>> RunAsync);

internal static class ConformanceOperationStrategy
{
    private static readonly IReadOnlyDictionary<string, ConformanceOperationDescriptor> Operations =
        new[]
        {
            new ConformanceOperationDescriptor(
                "run",
                "windows-smb-readiness-v1",
                "Invoke-WindowsSmbReadinessConformance.ps1",
                RunReadinessAsync),
            new ConformanceOperationDescriptor(
                "host-run",
                "windows-circle-files-host-v1",
                "Invoke-WindowsCircleFilesHostConformance.ps1",
                RunHostAsync),
        }.ToDictionary(operation => operation.CommandName, StringComparer.Ordinal);

    public static ConformanceOperationDescriptor? Resolve(string command) =>
        Operations.GetValueOrDefault(command);

    private static async Task<string> RunReadinessAsync(
        IConformanceProcessRunner processes,
        string guestScript,
        WindowsConformanceTargetProfile target,
        WindowsPackageIdentity package,
        string receiptPath,
        CancellationToken cancellationToken) =>
        (await new WindowsSmbReadinessConformanceRunner(processes, guestScript)
            .RunAsync(target, package, receiptPath, cancellationToken)
            .ConfigureAwait(false)).Outcome;

    private static async Task<string> RunHostAsync(
        IConformanceProcessRunner processes,
        string guestScript,
        WindowsConformanceTargetProfile target,
        WindowsPackageIdentity package,
        string receiptPath,
        CancellationToken cancellationToken) =>
        (await new WindowsCircleFilesHostConformanceRunner(processes, guestScript)
            .RunAsync(target, package, receiptPath, cancellationToken)
            .ConfigureAwait(false)).Outcome;
}
