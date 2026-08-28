using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Balls.WindowsConformance;

internal sealed class WindowsSmbReadinessConformanceRunner(
    IConformanceProcessRunner processes,
    string guestScript,
    TimeProvider? timeProvider = null)
{
    private const string Operation = "windows-smb-readiness-v1";
    private const int MaximumOutputBytes = 64 * 1024;
    private static readonly string[] ExpectedReadinessCheckIds =
    [
        "windows-platform",
        "smb-server",
        "smb-dialect",
        "smb1",
        "guest-access",
        "signing",
        "encryption",
        "private-network",
        "firewall-scope",
    ];
    private static readonly HashSet<string> GuestFailureCodes = new(StringComparer.Ordinal)
    {
        "target_precondition_mismatch",
        "package_identity_mismatch",
        "daemon_start_failed",
        "readiness_cli_failed",
        "cleanup_incomplete",
    };
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<WindowsSmbReadinessConformanceReceipt> RunAsync(
        WindowsConformanceTargetProfile target,
        WindowsPackageIdentity package,
        string receiptPath,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new ConformanceRefusalException("linux_required");
        }

        var startedAt = clock.GetUtcNow();
        var guestOperation = CompressGuestOperation(guestScript);
        var preflightResult = await processes.RunAsync(
            SshRequest(
                target,
                RemoteCommand(
                    guestOperation,
                    "preflight",
                    new Dictionary<string, string>(StringComparer.Ordinal)),
                TimeSpan.FromSeconds(30)),
            cancellationToken).ConfigureAwait(false);
        if (preflightResult.ExitCode != 0)
        {
            throw new ConformanceRefusalException("target_unavailable");
        }

        var preflight = ConformanceReceiptParser.Parse<GuestPreflightReceipt>(
            preflightResult.StandardOutput,
            "preflight_invalid");
        ValidatePreflight(preflight, target);

        var runId = Guid.NewGuid().ToString("N");
        var remotePackageName = $"balls-smb-readiness-{runId}.zip";
        var cleanupRequired = false;
        GuestRunReceipt? guestResult = null;
        Exception? failure = null;
        try
        {
            cleanupRequired = true;
            var transfer = await processes.RunAsync(
                ScpRequest(target, package.Path, remotePackageName),
                cancellationToken).ConfigureAwait(false);
            if (transfer.ExitCode != 0)
            {
                throw new ConformanceRefusalException("package_transfer_failed");
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BALLS_CONFORMANCE_RUN_ID"] = runId,
                ["BALLS_CONFORMANCE_EXPECTED_COMPUTER_NAME"] = target.ExpectedComputerName,
                ["BALLS_CONFORMANCE_EXPECTED_ACCOUNT_KIND"] = target.ExpectedAccountKind,
                ["BALLS_CONFORMANCE_STAGED_PACKAGE_NAME"] = remotePackageName,
                ["BALLS_CONFORMANCE_PACKAGE_NAME"] = package.FileName,
                ["BALLS_CONFORMANCE_PACKAGE_SHA256"] = package.Sha256,
                ["BALLS_CONFORMANCE_COMMIT"] = package.Commit,
            };
            var run = await processes.RunAsync(
                SshRequest(target, RemoteCommand(guestOperation, "run", values), TimeSpan.FromMinutes(3)),
                cancellationToken).ConfigureAwait(false);
            if (run.ExitCode != 0)
            {
                throw new ConformanceRefusalException(ParseGuestFailureCode(run.StandardOutput));
            }

            guestResult = ConformanceReceiptParser.Parse<GuestRunReceipt>(
                run.StandardOutput,
                "result_invalid");
            ValidateGuestResult(guestResult, target, package, preflight);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (cleanupRequired)
        {
            try
            {
                var cleanup = await processes.RunAsync(
                    SshRequest(
                        target,
                        RemoteCommand(
                            guestOperation,
                            "cleanup",
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["BALLS_CONFORMANCE_RUN_ID"] = runId,
                                ["BALLS_CONFORMANCE_STAGED_PACKAGE_NAME"] = remotePackageName,
                            }),
                        TimeSpan.FromSeconds(30)),
                    cancellationToken).ConfigureAwait(false);
                if (cleanup.ExitCode != 0)
                {
                    failure = new ConformanceRefusalException("cleanup_unconfirmed");
                }
            }
            catch (Exception)
            {
                failure = new ConformanceRefusalException("cleanup_unconfirmed");
            }
        }

        if (failure is not null)
        {
            throw failure;
        }

        var receipt = new WindowsSmbReadinessConformanceReceipt(
            "balls-windows-smb-readiness-conformance-v1",
            Operation,
            "passed",
            startedAt,
            clock.GetUtcNow(),
            new ConformanceSourceReceipt(
                package.Commit,
                package.FileName,
                package.Sha256,
                package.Version,
                package.Architecture),
            new ConformanceTargetReceipt(target.TargetId, target.ConnectivityPath, preflight),
            guestResult!.ProductReadiness,
            guestResult.NativeObservation,
            guestResult.NativeStateUnchanged,
            guestResult.Cleanup,
            guestResult.Limitations);
        WriteReceipt(receiptPath, receipt);
        return receipt;
    }

    private static void ValidatePreflight(
        GuestPreflightReceipt receipt,
        WindowsConformanceTargetProfile target)
    {
        if (receipt.Schema != "balls-windows-smb-readiness-preflight-v1"
            || receipt.Operation != Operation
            || receipt.Outcome != "ready"
            || !string.Equals(
                receipt.ComputerName,
                target.ExpectedComputerName,
                StringComparison.OrdinalIgnoreCase)
            || receipt.Account.Kind != target.ExpectedAccountKind
            || (target.ExpectedAccountKind == "administrator" && !receipt.Account.Elevated)
            || !receipt.DirtyState.Clean
            || receipt.DirtyState.ExistingBallsProcesses != 0
            || receipt.DirtyState.OwnedArtifacts != 0
            || string.IsNullOrWhiteSpace(receipt.Windows.ProductName)
            || string.IsNullOrWhiteSpace(receipt.Windows.BuildNumber)
            || string.IsNullOrWhiteSpace(receipt.Policy.ExecutionPolicy)
            || receipt.Network.Categories.Count == 0
            || receipt.Network.FirewallProfiles.Count == 0)
        {
            throw new ConformanceRefusalException("target_identity_or_precondition_mismatch");
        }
    }

    private static void ValidateGuestResult(
        GuestRunReceipt receipt,
        WindowsConformanceTargetProfile target,
        WindowsPackageIdentity package,
        GuestPreflightReceipt preflight)
    {
        ValidatePreflight(receipt.Preflight, target);
        var statuses = new[] { "ready", "not-ready", "unknown" };
        if (receipt.Schema != "balls-windows-smb-readiness-guest-v1"
            || receipt.Operation != Operation
            || receipt.Outcome != "passed"
            || !string.Equals(receipt.Preflight.ComputerName, preflight.ComputerName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(receipt.Product.Commit, package.Commit, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(receipt.Product.PackageSha256, package.Sha256, StringComparison.OrdinalIgnoreCase)
            || receipt.Product.PackageName != package.FileName
            || receipt.Product.Version != package.Version
            || string.IsNullOrWhiteSpace(receipt.Product.CliVersion)
            || string.IsNullOrWhiteSpace(receipt.Product.DaemonVersion)
            || receipt.ProductReadiness.Provider != "windows-smb-3.1.1-v1"
            || !statuses.Contains(receipt.ProductReadiness.Status, StringComparer.Ordinal)
            || !receipt.ProductReadiness.Checks
                .Select(check => check.Id)
                .SequenceEqual(ExpectedReadinessCheckIds, StringComparer.Ordinal)
            || receipt.ProductReadiness.Checks.Any(check =>
                string.IsNullOrWhiteSpace(check.Id)
                || !statuses.Contains(check.Status, StringComparer.Ordinal)
                || string.IsNullOrWhiteSpace(check.Code)
                || string.IsNullOrWhiteSpace(check.Summary))
            || !receipt.NativeStateUnchanged
            || !receipt.Cleanup.Complete
            || !receipt.Cleanup.DaemonStopped
            || !receipt.Cleanup.StateRemoved
            || !receipt.Cleanup.PackageRemoved
            || !receipt.Limitations.Contains("read-only Windows conformance; no operating-system mutation", StringComparer.Ordinal)
            || !receipt.Limitations.Contains("not GUI, UAC, Explorer, physical-device, or release acceptance", StringComparer.Ordinal))
        {
            throw new ConformanceRefusalException("result_identity_or_contract_mismatch");
        }
    }

    private static string ParseGuestFailureCode(string json)
    {
        var failure = ConformanceReceiptParser.Parse<GuestFailureReceipt>(
            json,
            "product_execution_failed");
        if (failure.Schema != "balls-windows-smb-readiness-guest-v1"
            || failure.Operation != Operation
            || failure.Outcome != "failed"
            || !GuestFailureCodes.Contains(failure.Code))
        {
            throw new ConformanceRefusalException("product_execution_failed");
        }

        return $"guest_{failure.Code}";
    }

    private static ConformanceProcessRequest SshRequest(
        WindowsConformanceTargetProfile target,
        string remoteCommand,
        TimeSpan timeout) =>
        new(
            "ssh",
            [
                .. CommonTransportArguments(target),
                "-p",
                target.Transport.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-T",
                $"{target.Transport.User}@{target.Transport.Host}",
                remoteCommand,
            ],
            timeout,
            MaximumOutputBytes);

    private static ConformanceProcessRequest ScpRequest(
        WindowsConformanceTargetProfile target,
        string packagePath,
        string remotePackageName) =>
        new(
            "scp",
            [
                .. CommonTransportArguments(target),
                "-q",
                "-B",
                "-P",
                target.Transport.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                packagePath,
                $"{target.Transport.User}@{target.Transport.Host}:{remotePackageName}",
            ],
            TimeSpan.FromMinutes(2),
            8192);

    private static IEnumerable<string> CommonTransportArguments(WindowsConformanceTargetProfile target) =>
    [
        "-F",
        "/dev/null",
        "-o",
        "BatchMode=yes",
        "-o",
        "ClearAllForwardings=yes",
        "-o",
        "StrictHostKeyChecking=yes",
        "-o",
        $"UserKnownHostsFile={target.Transport.KnownHostsFile}",
        "-o",
        "IdentitiesOnly=yes",
        "-o",
        $"IdentityFile={target.Transport.PublicKeyFile}",
        "-o",
        "ConnectTimeout=10",
        "-o",
        "ConnectionAttempts=1",
        "-o",
        "ServerAliveInterval=5",
        "-o",
        "ServerAliveCountMax=2",
        "-o",
        "LogLevel=ERROR",
    ];

    private static string RemoteCommand(
        string guestOperation,
        string mode,
        IReadOnlyDictionary<string, string> values)
    {
        var assignments = new StringBuilder($"set BALLS_CONFORMANCE_MODE={mode}&&");
        foreach (var value in values)
        {
            if (value.Value.Any(character =>
                    !char.IsAsciiLetterOrDigit(character)
                    && character is not ('.' or '-' or '_')))
            {
                throw new ConformanceRefusalException("remote_value_invalid");
            }

            assignments.Append("set ");
            assignments.Append(value.Key);
            assignments.Append('=');
            assignments.Append(value.Value);
            assignments.Append("&&");
        }

        assignments.Append("powershell.exe -NoLogo -NoProfile -NonInteractive -Command \"");
        assignments.Append(guestOperation);
        assignments.Append('"');
        return assignments.ToString();
    }

    private static string CompressGuestOperation(string script)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(script));
        }

        var payload = Convert.ToBase64String(compressed.ToArray());
        var loader =
            "$bytes=[Convert]::FromBase64String('" + payload + "');" +
            "$memory=[IO.MemoryStream]::new($bytes);" +
            "$gzip=[IO.Compression.GzipStream]::new($memory,[IO.Compression.CompressionMode]::Decompress);" +
            "$reader=[IO.StreamReader]::new($gzip,[Text.Encoding]::UTF8);" +
            "try{$script=$reader.ReadToEnd();& ([ScriptBlock]::Create($script))}" +
            "finally{$reader.Dispose();$gzip.Dispose();$memory.Dispose()}";
        if (loader.Length > 7000 || loader.Contains('"'))
        {
            throw new ConformanceRefusalException("guest_operation_oversized");
        }

        return loader;
    }

    private static void WriteReceipt(
        string receiptPath,
        WindowsSmbReadinessConformanceReceipt receipt)
    {
        var path = Path.GetFullPath(receiptPath);
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory) || File.Exists(path))
        {
            throw new ConformanceRefusalException("receipt_path_unsafe");
        }

        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(
                    receipt,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }) + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
