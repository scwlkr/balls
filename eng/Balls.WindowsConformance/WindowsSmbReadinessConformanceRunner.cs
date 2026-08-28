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
        "package_probe_timeout",
        "daemon_start_failed",
        "daemon_start_win32",
        "daemon_start_invalid_operation",
        "daemon_start_io",
        "daemon_start_unauthorized",
        "daemon_start_other",
        "daemon_exited_usage",
        "daemon_exited_startup",
        "daemon_exited_unsupported",
        "daemon_exited_unexpected",
        "daemon_exited_after_ready",
        "daemon_exited_clean_before_ready",
        "daemon_exited_dotnet_invalid_operation",
        "daemon_exited_dotnet_platform_unsupported",
        "daemon_exited_dotnet_dpapi",
        "daemon_exited_dotnet_cryptographic",
        "daemon_exited_dotnet_dependency",
        "daemon_exited_dotnet_unauthorized",
        "daemon_exited_dotnet_io",
        "daemon_exited_dotnet_type_initialization",
        "daemon_exited_dotnet_argument",
        "daemon_exited_dotnet_output_oversized",
        "daemon_exited_dotnet_other",
        "daemon_readiness_timeout",
        "readiness_cli_timeout",
        "readiness_cli_failed",
        "native_inspection_timeout",
        "native_inspection_failed",
        "cleanup_incomplete",
        "guest_operation_unhandled_initializing",
        "guest_operation_unhandled_environment",
        "guest_operation_unhandled_preflight",
        "guest_operation_unhandled_preconditions",
        "guest_operation_unhandled_package",
        "guest_operation_unhandled_native_before",
        "guest_operation_unhandled_daemon_start",
        "guest_operation_unhandled_daemon_poll",
        "guest_operation_unhandled_readiness",
        "guest_operation_unhandled_native_after",
        "guest_operation_unhandled_cleanup",
        "guest_operation_unhandled_receipt",
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
        var preflightResult = await processes.RunAsync(
            SshRequest(
                target,
                RemoteCommand(
                    "preflight",
                    new Dictionary<string, string>(StringComparer.Ordinal)),
                TimeSpan.FromSeconds(30),
                guestScript),
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
                SshRequest(
                    target,
                    RemoteCommand("run", values),
                    TimeSpan.FromMinutes(3),
                    guestScript),
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
                            "cleanup",
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["BALLS_CONFORMANCE_RUN_ID"] = runId,
                                ["BALLS_CONFORMANCE_STAGED_PACKAGE_NAME"] = remotePackageName,
                            }),
                        TimeSpan.FromSeconds(60),
                        guestScript),
                    CancellationToken.None).ConfigureAwait(false);
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

        var completed = guestResult!;
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
                package.Architecture,
                completed.Product.DaemonPrivilege),
            new ConformanceTargetReceipt(target.TargetId, target.ConnectivityPath, preflight),
            completed.ProductReadiness,
            completed.NativeObservation,
            completed.NativeStateUnchanged,
            completed.Cleanup,
            completed.Limitations);
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
            || receipt.Product.DaemonPrivilege != "unelevated"
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

        ValidateNativeCorroboration(receipt);
    }

    private static void ValidateNativeCorroboration(GuestRunReceipt receipt)
    {
        var checks = receipt.ProductReadiness.Checks.ToDictionary(
            check => check.Id,
            StringComparer.Ordinal);
        var native = receipt.NativeObservation;
        var platformReady = int.TryParse(
                receipt.Preflight.Windows.BuildNumber,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var buildNumber)
            && buildNumber >= 26100
            && receipt.Preflight.Windows.InstallationType is "Client" or "Server" or "Server Core";
        var corroborated = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["windows-platform"] = platformReady,
            ["smb-server"] = native.ServerSmb2Enabled,
            ["smb-dialect"] = native.ServerSmb2Enabled,
            ["smb1"] = native.ServerSmb1FeatureState == "disabled",
            ["guest-access"] = native.ServerSigningRequired
                && native.ServerEncryptionSupported
                && native.ServerRejectsUnencryptedAccess
                && !native.InsecureGuestLogonsEnabled,
            ["signing"] = native.ServerSigningRequired,
            ["encryption"] = native.ServerEncryptionSupported
                && native.ServerRejectsUnencryptedAccess,
            ["private-network"] = native.NetworkCategories.Contains(
                "private",
                StringComparer.Ordinal),
            ["firewall-scope"] = native.FirewallProfiles.Contains(
                    "private",
                    StringComparer.Ordinal)
                && native.FirewallProfiles.Contains("public", StringComparer.Ordinal)
                && native.PublicSmbAllowRules == 0,
        };

        if (checks.Any(pair => pair.Value.Status == "ready" && !corroborated[pair.Key]))
        {
            throw new ConformanceRefusalException("native_corroboration_mismatch");
        }
    }

    private static string ParseGuestFailureCode(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "guest_failure_output_empty";
        }

        if (json.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length != 1)
        {
            return "guest_failure_output_contaminated";
        }

        GuestFailureReceipt failure;
        try
        {
            failure = ConformanceReceiptParser.Parse<GuestFailureReceipt>(
                json,
                "product_execution_failed");
        }
        catch (ConformanceRefusalException exception)
            when (exception.Code == "product_execution_failed")
        {
            return "guest_failure_output_invalid";
        }

        if (failure.Schema != "balls-windows-smb-readiness-guest-v1"
            || failure.Operation != Operation
            || failure.Outcome != "failed"
            || !IsGuestFailureCode(failure.Code))
        {
            throw new ConformanceRefusalException("product_execution_failed");
        }

        return $"guest_{failure.Code}";
    }

    private static bool IsGuestFailureCode(string code)
    {
        const string statusPrefix = "daemon_exited_status_";
        if (GuestFailureCodes.Contains(code))
        {
            return true;
        }

        if (code.Length != statusPrefix.Length + 8
            || !code.StartsWith(statusPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in code.AsSpan(statusPrefix.Length))
        {
            if (character is not (>= '0' and <= '9')
                and not (>= 'A' and <= 'F'))
            {
                return false;
            }
        }

        return true;
    }

    private static ConformanceProcessRequest SshRequest(
        WindowsConformanceTargetProfile target,
        string remoteCommand,
        TimeSpan timeout,
        string standardInput) =>
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
            MaximumOutputBytes,
            standardInput);

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

        assignments.Append(
            "powershell.exe -NoLogo -NoProfile -NonInteractive " +
            "-Command \"$script=[Console]::In.ReadToEnd();&([scriptblock]::Create($script))\"");
        return assignments.ToString();
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
