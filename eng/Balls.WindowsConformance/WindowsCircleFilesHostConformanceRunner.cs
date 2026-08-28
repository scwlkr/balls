using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Balls.WindowsConformance;

internal sealed class WindowsCircleFilesHostConformanceRunner(
    IConformanceProcessRunner processes,
    string guestScript,
    TimeProvider? timeProvider = null)
{
    private const string Operation = "windows-circle-files-host-v1";
    private const string StorageInspectionOperation =
        "windows-circle-files-host-storage-inspection-v1";
    private const string ScriptEndMarker = "__BALLS_HOST_CONFORMANCE_OPERATION_END__";
    private const int MaximumOutputBytes = 64 * 1024;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<WindowsCircleFilesHostStorageInspectionReceipt> InspectStorageAsync(
        WindowsConformanceTargetProfile target,
        string sourceCommit,
        string receiptPath,
        CancellationToken cancellationToken)
    {
        EnsureReceiptPathSafe(receiptPath);
        if (target.Operation != StorageInspectionOperation
            || target.DisposablePath is null
            || target.ExpectedVolumeIdentitySha256 is not null
            || target.ExpectedDiskIdentitySha256 is not null
            || !IsCommit(sourceCommit))
        {
            throw new ConformanceRefusalException("operation_not_allowed");
        }

        var startedAt = clock.GetUtcNow();
        var result = await processes.RunAsync(
            SshRequest(
                target.Transport,
                RemoteCommand("storage-inspect", new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["BALLS_CONFORMANCE_DISPOSABLE_PATH_B64"] = EncodeValue(target.DisposablePath),
                }),
                TimeSpan.FromSeconds(45)),
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new ConformanceRefusalException("storage_inspection_failed");
        }

        var guest = ConformanceReceiptParser.Parse<GuestHostStorageInspectionReceipt>(
            result.StandardOutput,
            "storage_inspection_invalid");
        if (guest.Schema != "balls-windows-host-storage-inspection-v1"
            || guest.Operation != StorageInspectionOperation
            || guest.Outcome != "observed"
            || !string.Equals(guest.ComputerName, target.ExpectedComputerName, StringComparison.OrdinalIgnoreCase)
            || guest.Account.Kind != target.ExpectedAccountKind
            || !guest.Account.Elevated
            || guest.Account.Integrity != "high"
            || !IsSha256(guest.Account.IdentitySha256)
            || guest.PathIdentitySha256 != PathIdentity(target.DisposablePath)
            || !StorageSafe(guest.Storage))
        {
            throw new ConformanceRefusalException("storage_inspection_invalid");
        }

        var receipt = new WindowsCircleFilesHostStorageInspectionReceipt(
            "balls-windows-circle-files-host-storage-inspection-v1",
            StorageInspectionOperation,
            "observed",
            startedAt,
            clock.GetUtcNow(),
            sourceCommit,
            target.TargetId,
            target.ConnectivityPath,
            guest.PathIdentitySha256,
            guest.ComputerName,
            guest.Account,
            guest.Storage,
            [],
            [
                "read-only hash derivation for one exact absent authorized path",
                "does not authorize host mutation or prove a lifecycle",
            ]);
        WriteStorageReceipt(receiptPath, receipt);
        return receipt;
    }

    public async Task<WindowsCircleFilesHostConformanceReceipt> RunAsync(
        WindowsConformanceTargetProfile target,
        WindowsPackageIdentity package,
        string receiptPath,
        CancellationToken cancellationToken)
    {
        EnsureReceiptPathSafe(receiptPath);
        if (target.Operation != Operation
            || target.DisposablePath is null
            || target.ExpectedVolumeIdentitySha256 is null
            || target.ExpectedDiskIdentitySha256 is null)
        {
            throw new ConformanceRefusalException("operation_not_allowed");
        }

        var startedAt = clock.GetUtcNow();
        var phase = "target-preflight";
        var runId = Guid.NewGuid().ToString("N");
        var remotePackageName = $"balls-host-conformance-{runId}.zip";
        var transferred = false;
        GuestPreflightReceipt? inspectionPreflight = null;
        GuestPreflightReceipt? productPreflight = null;
        GuestHostPrepareReceipt? prepared = null;
        GuestHostRefusalReceipt? refused = null;
        GuestHostApplyReceipt? applied = null;
        GuestHostRemovalReceipt? removed = null;
        GuestHostNativeObservation? nativePrepared = null;
        GuestHostNativeObservation? nativeRolledBack = null;
        GuestHostNativeObservation? nativeProvisioned = null;
        GuestHostNativeObservation? nativeFinal = null;
        var cleanup = EmptyCleanup("not_started");

        try
        {
            inspectionPreflight = await PreflightAsync(
                target,
                target.Transport,
                "preflight",
                target.ExpectedAccountKind,
                expectedIdentitySha256: null,
                cancellationToken).ConfigureAwait(false);

            phase = "product-preflight";
            productPreflight = await PreflightAsync(
                target,
                target.ProductTransport,
                "product-preflight",
                "administrator",
                target.ExpectedProductAccountSidSha256,
                cancellationToken).ConfigureAwait(false);

            phase = "package-transfer";
            var transfer = await processes.RunAsync(
                ScpRequest(target.ProductTransport, package.Path, remotePackageName),
                cancellationToken).ConfigureAwait(false);
            if (transfer.ExitCode != 0)
            {
                throw new ConformanceRefusalException("package_transfer_failed");
            }
            transferred = true;

            var common = CommonGuestValues(target, package, runId, remotePackageName);
            phase = "product-prepare";
            prepared = await RunProductAsync<GuestHostPrepareReceipt>(
                target,
                "prepare",
                common,
                TimeSpan.FromMinutes(3),
                "prepare_failed",
                cancellationToken).ConfigureAwait(false);
            ValidatePrepare(prepared, target, package, productPreflight);

            var nativeValues = NativeValues(target, prepared.Context, prepared.Seed, runId);
            phase = "native-prepared";
            nativePrepared = await ObserveNativeAsync(
                target,
                "prepared",
                nativeValues,
                cancellationToken).ConfigureAwait(false);
            ValidateUnprovisionedNative(nativePrepared, target, prepared, "prepared");

            phase = "injected-failure";
            refused = await RunProductAsync<GuestHostRefusalReceipt>(
                target,
                "inject-failure",
                common,
                TimeSpan.FromMinutes(3),
                "injected_failure_failed",
                cancellationToken).ConfigureAwait(false);
            ValidateRefusal(refused);

            phase = "native-rollback";
            nativeRolledBack = await ObserveNativeAsync(
                target,
                "rolled-back",
                nativeValues,
                cancellationToken).ConfigureAwait(false);
            ValidateUnprovisionedNative(nativeRolledBack, target, prepared, "rolled-back");
            if (nativeRolledBack.AclSha256 != nativePrepared.AclSha256
                || nativeRolledBack.UnrelatedState != nativePrepared.UnrelatedState)
            {
                throw new ConformanceRefusalException("injected_failure_rollback_mismatch");
            }

            phase = "product-apply";
            applied = await RunProductAsync<GuestHostApplyReceipt>(
                target,
                "apply",
                common,
                TimeSpan.FromMinutes(3),
                "apply_failed",
                cancellationToken).ConfigureAwait(false);
            ValidateApply(applied, prepared);

            phase = "native-provisioned";
            nativeProvisioned = await ObserveNativeAsync(
                target,
                "provisioned",
                nativeValues,
                cancellationToken).ConfigureAwait(false);
            ValidateProvisionedNative(
                nativeProvisioned,
                nativePrepared.UnrelatedState,
                target,
                prepared);

            phase = "product-remove";
            removed = await RunProductAsync<GuestHostRemovalReceipt>(
                target,
                "remove",
                common,
                TimeSpan.FromMinutes(3),
                "remove_failed",
                cancellationToken).ConfigureAwait(false);
            ValidateRemoval(removed);
            cleanup = FromRemoval(removed);

            phase = "native-final";
            nativeFinal = await ObserveNativeAsync(
                target,
                "final",
                nativeValues,
                cancellationToken).ConfigureAwait(false);
            ValidateUnprovisionedNative(nativeFinal, target, prepared, "final");

            var unchanged = nativeFinal.UnrelatedState == nativePrepared.UnrelatedState;
            var seedPreserved = SeedsEqual(nativeFinal.Seed, prepared.Seed);
            var aclRestored = nativeFinal.AclSha256 == nativePrepared.AclSha256;
            if (!unchanged || !seedPreserved || !aclRestored || !cleanup.Complete)
            {
                throw new ConformanceRefusalException("final_cleanup_mismatch");
            }

            var receipt = CreateReceipt(
                "passed",
                "passed",
                "complete",
                startedAt,
                package,
                target,
                inspectionPreflight,
                productPreflight,
                prepared,
                refused,
                applied,
                removed,
                nativePrepared,
                nativeRolledBack,
                nativeProvisioned,
                nativeFinal,
                cleanup);
            WriteReceipt(receiptPath, receipt);
            return receipt;
        }
        catch (Exception exception)
        {
            var code = exception is ConformanceRefusalException refusal
                ? refusal.Code
                : exception is OperationCanceledException && cancellationToken.IsCancellationRequested
                    ? "cancelled"
                    : "unexpected_failure";

            if (transferred)
            {
                cleanup = await TryCleanupAsync(
                    target,
                    CommonGuestValues(target, package, runId, remotePackageName))
                    .ConfigureAwait(false);
                if (!cleanup.Complete)
                {
                    code = "cleanup_unconfirmed";
                }
            }

            var partial = CreateReceipt(
                "failed",
                code,
                phase,
                startedAt,
                package,
                target,
                inspectionPreflight,
                productPreflight,
                prepared,
                refused,
                applied,
                removed,
                nativePrepared,
                nativeRolledBack,
                nativeProvisioned,
                nativeFinal,
                cleanup);
            WriteReceipt(receiptPath, partial);
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            throw new ConformanceRefusalException(code);
        }
    }

    private async Task<GuestPreflightReceipt> PreflightAsync(
        WindowsConformanceTargetProfile target,
        WindowsConformanceSshTransport transport,
        string mode,
        string expectedAccountKind,
        string? expectedIdentitySha256,
        CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync(
            SshRequest(
                transport,
                RemoteCommand(mode, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["BALLS_CONFORMANCE_DISPOSABLE_PATH_B64"] = EncodeValue(target.DisposablePath!),
                    ["BALLS_CONFORMANCE_EXPECTED_VOLUME_SHA256"] = target.ExpectedVolumeIdentitySha256!,
                    ["BALLS_CONFORMANCE_EXPECTED_DISK_SHA256"] = target.ExpectedDiskIdentitySha256!,
                }),
                TimeSpan.FromSeconds(45)),
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new ConformanceRefusalException(
                mode == "preflight" ? "target_unavailable" : "product_target_unavailable");
        }

        var receipt = ConformanceReceiptParser.Parse<GuestPreflightReceipt>(
            result.StandardOutput,
            "preflight_invalid");
        if (receipt.Schema != "balls-windows-host-preflight-v1"
            || receipt.Operation != Operation
            || receipt.Outcome != "ready"
            || !string.Equals(receipt.ComputerName, target.ExpectedComputerName, StringComparison.OrdinalIgnoreCase)
            || receipt.Account.Kind != expectedAccountKind
            || !receipt.Account.Elevated
            || receipt.Account.Integrity != "high"
            || (expectedIdentitySha256 is not null
                && receipt.Account.IdentitySha256 != expectedIdentitySha256)
            || receipt.Account.IdentitySha256.Length != 64
            || !receipt.DirtyState.Clean
            || receipt.DirtyState.ExistingBallsProcesses != 0
            || receipt.DirtyState.OwnedArtifacts != 0
            || !StorageMatches(receipt.Storage, target)
            || string.IsNullOrWhiteSpace(receipt.Policy.ExecutionPolicy)
            || receipt.Network.Categories.Count == 0
            || receipt.Network.FirewallProfiles.Count == 0)
        {
            throw new ConformanceRefusalException("target_identity_or_precondition_mismatch");
        }
        return receipt;
    }

    private async Task<T> RunProductAsync<T>(
        WindowsConformanceTargetProfile target,
        string mode,
        IReadOnlyDictionary<string, string> values,
        TimeSpan timeout,
        string failureCode,
        CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync(
            SshRequest(target.ProductTransport, RemoteCommand(mode, values), timeout),
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new ConformanceRefusalException(ParseGuestFailure(result.StandardOutput, failureCode));
        }
        return ConformanceReceiptParser.Parse<T>(result.StandardOutput, failureCode);
    }

    private async Task<GuestHostNativeObservation> ObserveNativeAsync(
        WindowsConformanceTargetProfile target,
        string state,
        IReadOnlyDictionary<string, string> baseValues,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(baseValues, StringComparer.Ordinal)
        {
            ["BALLS_CONFORMANCE_NATIVE_STATE"] = state,
        };
        var result = await processes.RunAsync(
            SshRequest(target.Transport, RemoteCommand("native", values), TimeSpan.FromSeconds(60)),
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new ConformanceRefusalException(ParseGuestFailure(
                result.StandardOutput,
                "native_inspection_failed"));
        }
        var receipt = ConformanceReceiptParser.Parse<GuestHostNativeReceipt>(
            result.StandardOutput,
            "native_inspection_invalid");
        if (receipt.Schema != "balls-windows-host-native-v1"
            || receipt.Operation != Operation
            || receipt.Outcome != "observed"
            || receipt.Observation.State != state)
        {
            throw new ConformanceRefusalException("native_inspection_invalid");
        }
        return receipt.Observation;
    }

    private async Task<HostConformanceCleanup> TryCleanupAsync(
        WindowsConformanceTargetProfile target,
        IReadOnlyDictionary<string, string> values)
    {
        try
        {
            var result = await processes.RunAsync(
                SshRequest(target.ProductTransport, RemoteCommand("cleanup", values), TimeSpan.FromMinutes(3)),
                CancellationToken.None).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return EmptyCleanup("cleanup_failed");
            }
            var receipt = ConformanceReceiptParser.Parse<GuestHostCleanupReceipt>(
                result.StandardOutput,
                "cleanup_invalid");
            if (receipt.Schema != "balls-windows-host-cleanup-v1"
                || receipt.Operation != Operation)
            {
                return EmptyCleanup("cleanup_invalid");
            }
            return new HostConformanceCleanup(
                receipt.ProductRemovalAttempted,
                receipt.ProductResourcesRemoved,
                receipt.Cleanup.DaemonStopped,
                receipt.Cleanup.StateRemoved,
                receipt.Cleanup.PackageRemoved,
                receipt.Outcome == "clean" && receipt.Cleanup.Complete,
                receipt.Code);
        }
        catch (Exception)
        {
            return EmptyCleanup("cleanup_unconfirmed");
        }
    }

    private static IReadOnlyDictionary<string, string> CommonGuestValues(
        WindowsConformanceTargetProfile target,
        WindowsPackageIdentity package,
        string runId,
        string remotePackageName) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BALLS_CONFORMANCE_RUN_ID"] = runId,
            ["BALLS_CONFORMANCE_EXPECTED_COMPUTER_NAME"] = target.ExpectedComputerName,
            ["BALLS_CONFORMANCE_EXPECTED_PRODUCT_SID_SHA256"] = target.ExpectedProductAccountSidSha256,
            ["BALLS_CONFORMANCE_DISPOSABLE_PATH_B64"] = EncodeValue(target.DisposablePath!),
            ["BALLS_CONFORMANCE_EXPECTED_VOLUME_SHA256"] = target.ExpectedVolumeIdentitySha256!,
            ["BALLS_CONFORMANCE_EXPECTED_DISK_SHA256"] = target.ExpectedDiskIdentitySha256!,
            ["BALLS_CONFORMANCE_STAGED_PACKAGE_NAME"] = remotePackageName,
            ["BALLS_CONFORMANCE_PACKAGE_NAME"] = package.FileName,
            ["BALLS_CONFORMANCE_PACKAGE_SHA256"] = package.Sha256,
            ["BALLS_CONFORMANCE_COMMIT"] = package.Commit,
        };

    private static IReadOnlyDictionary<string, string> NativeValues(
        WindowsConformanceTargetProfile target,
        GuestHostContext context,
        GuestSeedObservation seed,
        string runId) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BALLS_CONFORMANCE_RUN_ID"] = runId,
            ["BALLS_CONFORMANCE_DISPOSABLE_PATH_B64"] = EncodeValue(target.DisposablePath!),
            ["BALLS_CONFORMANCE_EXPECTED_VOLUME_SHA256"] = target.ExpectedVolumeIdentitySha256!,
            ["BALLS_CONFORMANCE_EXPECTED_DISK_SHA256"] = target.ExpectedDiskIdentitySha256!,
            ["BALLS_CONFORMANCE_EXPECTED_PRODUCT_SID_SHA256"] = target.ExpectedProductAccountSidSha256,
            ["BALLS_CONFORMANCE_CIRCLE_ID"] = context.CircleId,
            ["BALLS_CONFORMANCE_CONTRIBUTION_ID"] = context.ContributionId,
            ["BALLS_CONFORMANCE_PLAN_ID"] = context.PlanId,
            ["BALLS_CONFORMANCE_SHARE_NAME"] = context.ShareName,
            ["BALLS_CONFORMANCE_FIREWALL_RULE_NAME"] = context.FirewallRuleName,
            ["BALLS_CONFORMANCE_OWNERSHIP_ID"] = context.OwnershipId,
            ["BALLS_CONFORMANCE_SEED_SHA256"] = seed.Sha256,
            ["BALLS_CONFORMANCE_SEED_LENGTH"] = seed.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

    private static void ValidatePrepare(
        GuestHostPrepareReceipt receipt,
        WindowsConformanceTargetProfile target,
        WindowsPackageIdentity package,
        GuestPreflightReceipt productPreflight)
    {
        if (receipt.Schema != "balls-windows-host-prepare-v1"
            || receipt.Operation != Operation
            || receipt.Outcome != "prepared"
            || receipt.Preflight.Schema != "balls-windows-host-preflight-v1"
            || receipt.Preflight.Account.IdentitySha256 != productPreflight.Account.IdentitySha256
            || receipt.Product.Commit != package.Commit
            || !receipt.Product.PackageSha256.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase)
            || receipt.Product.PackageName != package.FileName
            || receipt.Product.Version != package.Version
            || receipt.Product.DaemonPrivilege != "administrative"
            || receipt.Product.BuildConfiguration != "debug-conformance"
            || string.IsNullOrWhiteSpace(receipt.Product.CliVersion)
            || string.IsNullOrWhiteSpace(receipt.Product.DaemonVersion)
            || receipt.Context.FolderPath != target.DisposablePath
            || !Guid.TryParseExact(receipt.Context.CircleId, "D", out _)
            || !Guid.TryParseExact(receipt.Context.ContributionId, "D", out _)
            || !IsSha256(receipt.Context.PlanId)
            || !IsSha256(receipt.Context.OwnershipId)
            || !IsSeedValid(receipt.Seed))
        {
            throw new ConformanceRefusalException("prepare_identity_or_contract_mismatch");
        }
    }

    private static void ValidateRefusal(GuestHostRefusalReceipt receipt)
    {
        if (receipt.Schema != "balls-windows-host-refusal-v1"
            || receipt.Operation != Operation
            || receipt.Outcome != "rolled-back"
            || receipt.PlanMismatchCode != "hosting_plan_changed"
            || receipt.InjectedFailureCode != "hosting_apply_failed")
        {
            throw new ConformanceRefusalException("refusal_contract_mismatch");
        }
    }

    private static void ValidateApply(GuestHostApplyReceipt receipt, GuestHostPrepareReceipt prepared)
    {
        if (receipt.Schema != "balls-windows-host-apply-v1"
            || receipt.Operation != Operation
            || receipt.Outcome != "provisioned"
            || receipt.ApplyStatus != "applied"
            || receipt.RetryStatus != "already-applied"
            || receipt.PlanId != prepared.Context.PlanId)
        {
            throw new ConformanceRefusalException("apply_contract_mismatch");
        }
    }

    private static void ValidateRemoval(GuestHostRemovalReceipt receipt)
    {
        if (receipt.Schema != "balls-windows-host-removal-v1"
            || receipt.Operation != Operation
            || receipt.Outcome != "removed"
            || receipt.RemovalStatus is not ("removed" or "already-removed")
            || receipt.OpenSessionCount != 0
            || !IsSha256(receipt.PlanId)
            || !receipt.Cleanup.Complete
            || !receipt.Cleanup.DaemonStopped
            || !receipt.Cleanup.StateRemoved
            || !receipt.Cleanup.PackageRemoved)
        {
            throw new ConformanceRefusalException("removal_contract_mismatch");
        }
    }

    private static void ValidateUnprovisionedNative(
        GuestHostNativeObservation native,
        WindowsConformanceTargetProfile target,
        GuestHostPrepareReceipt prepared,
        string state)
    {
        ValidateNativeCommon(native, target, prepared, state);
        if (native.MarkerExists
            || native.JournalExists
            || native.FirewallRecoveryExists
            || native.ShareCount != 0
            || native.FirewallRuleCount != 0)
        {
            throw new ConformanceRefusalException("native_cleanup_mismatch");
        }
    }

    private static void ValidateProvisionedNative(
        GuestHostNativeObservation native,
        GuestUnrelatedStateFingerprint preparedUnrelatedState,
        WindowsConformanceTargetProfile target,
        GuestHostPrepareReceipt prepared)
    {
        ValidateNativeCommon(native, target, prepared, "provisioned");
        if (!native.AclProtected
            || native.OwnerSidSha256 != target.ExpectedProductAccountSidSha256
            || !native.OwnerFullControl
            || !native.SystemFullControl
            || native.AclAccessRuleCount != 2
            || native.AclApplicableRuleCount != 2
            || native.AclDenyRuleCount != 0
            || !native.AclShapeExact
            || native.UnrelatedState != preparedUnrelatedState
            || !native.MarkerExists
            || !native.MarkerMatches
            || !native.JournalExists
            || !native.JournalMatches
            || native.FirewallRecoveryExists
            || native.ShareCount != 1
            || !native.SharePathMatches
            || !native.ShareEncryptionRequired
            || native.ShareAccessCount != 1
            || !native.ShareAccessRestrictedToOwner
            || native.FirewallRuleCount != 1
            || !native.FirewallPrivateOnly
            || !native.FirewallLocalSubnetOnly
            || !native.FirewallTcp445Only
            || !native.FirewallLanmanServerOnly)
        {
            throw new ConformanceRefusalException("native_provisioning_mismatch");
        }
    }

    private static void ValidateNativeCommon(
        GuestHostNativeObservation native,
        WindowsConformanceTargetProfile target,
        GuestHostPrepareReceipt prepared,
        string state)
    {
        if (native.State != state
            || native.PathIdentitySha256 != PathIdentity(target.DisposablePath!)
            || !native.FolderExists
            || native.FolderReparsePoint
            || !IsSha256(native.FolderInventorySha256)
            || native.FolderInventoryCount != (state == "provisioned" ? 3 : 1)
            || !native.FolderInventoryExact
            || !SeedsEqual(native.Seed, prepared.Seed)
            || !IsSha256(native.AclSha256)
            || !IsSha256(native.OwnerSidSha256)
            || !UnrelatedStateValid(native.UnrelatedState))
        {
            throw new ConformanceRefusalException("native_identity_mismatch");
        }
    }

    private static WindowsCircleFilesHostConformanceReceipt CreateReceipt(
        string outcome,
        string code,
        string phase,
        DateTimeOffset startedAt,
        WindowsPackageIdentity package,
        WindowsConformanceTargetProfile target,
        GuestPreflightReceipt? inspectionPreflight,
        GuestPreflightReceipt? productPreflight,
        GuestHostPrepareReceipt? prepared,
        GuestHostRefusalReceipt? refused,
        GuestHostApplyReceipt? applied,
        GuestHostRemovalReceipt? removed,
        GuestHostNativeObservation? nativePrepared,
        GuestHostNativeObservation? nativeRolledBack,
        GuestHostNativeObservation? nativeProvisioned,
        GuestHostNativeObservation? nativeFinal,
        HostConformanceCleanup cleanup)
    {
        var targetReceipt = inspectionPreflight is not null && productPreflight is not null
            ? new ConformanceTargetReceipt(
                target.TargetId,
                target.ConnectivityPath,
                inspectionPreflight,
                productPreflight)
            : null;
        var outcomes = refused is not null && applied is not null && removed is not null
            ? new HostConformanceProductOutcomes(
                refused.PlanMismatchCode,
                refused.InjectedFailureCode,
                applied.ApplyStatus,
                applied.RetryStatus,
                removed.RemovalStatus)
            : null;
        var native = nativePrepared is not null
            && nativeRolledBack is not null
            && nativeProvisioned is not null
            && nativeFinal is not null
            ? new HostConformanceNativeEvidence(
                nativePrepared,
                nativeRolledBack,
                nativeProvisioned,
                nativeFinal,
                nativePrepared.UnrelatedState == nativeRolledBack.UnrelatedState
                    && nativePrepared.UnrelatedState == nativeProvisioned.UnrelatedState
                    && nativePrepared.UnrelatedState == nativeFinal.UnrelatedState,
                prepared is not null && SeedsEqual(prepared.Seed, nativeFinal.Seed),
                nativePrepared.AclSha256 == nativeFinal.AclSha256)
            : null;
        return new WindowsCircleFilesHostConformanceReceipt(
            "balls-windows-circle-files-host-conformance-v1",
            Operation,
            outcome,
            code,
            phase,
            startedAt,
            DateTimeOffset.UtcNow,
            new ConformanceSourceReceipt(
                package.Commit,
                package.FileName,
                package.Sha256,
                package.Version,
                package.Architecture,
                "administrative"),
            targetReceipt,
            target.DisposablePath!,
            prepared?.Product,
            prepared?.Context,
            prepared?.Seed,
            outcomes,
            native,
            cleanup,
            [],
            [
                "headless administrative context was independently authorized before execution",
                "does not prove user-visible UAC consent",
                "not native picker, GUI, Explorer, Member mapping, physical-device, or release acceptance",
            ]);
    }

    private static HostConformanceCleanup FromRemoval(GuestHostRemovalReceipt receipt) =>
        new(
            true,
            true,
            receipt.Cleanup.DaemonStopped,
            receipt.Cleanup.StateRemoved,
            receipt.Cleanup.PackageRemoved,
            receipt.Cleanup.Complete,
            receipt.Cleanup.Complete ? "clean" : "cleanup_incomplete");

    private static HostConformanceCleanup EmptyCleanup(string code) =>
        new(false, false, false, false, false, false, code);

    private static bool IsSeedValid(GuestSeedObservation seed) =>
        seed.FileName == "before-balls.txt" && seed.Length > 0 && IsSha256(seed.Sha256);

    private static bool SeedsEqual(GuestSeedObservation left, GuestSeedObservation right) =>
        left.FileName == right.FileName && left.Length == right.Length && left.Sha256 == right.Sha256;

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsCommit(string value) =>
        value.Length == 40 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool StorageMatches(
        GuestStorageObservation? storage,
        WindowsConformanceTargetProfile target) =>
        storage is
        {
            LocalDiskBacked: true,
            FileSystem: "NTFS" or "ReFS",
        }
        && storage.VolumeIdentitySha256 == target.ExpectedVolumeIdentitySha256
        && storage.DiskIdentitySha256 == target.ExpectedDiskIdentitySha256
        && !string.IsNullOrWhiteSpace(storage.BusType);

    private static bool StorageSafe(GuestStorageObservation storage) =>
        storage is
        {
            LocalDiskBacked: true,
            FileSystem: "NTFS" or "ReFS",
            BusType: "ATA" or "NVMe" or "RAID" or "SAS" or "SATA" or "SCSI",
        }
        && IsSha256(storage.VolumeIdentitySha256)
        && IsSha256(storage.DiskIdentitySha256);

    private static bool UnrelatedStateValid(GuestUnrelatedStateFingerprint state) =>
        IsSha256(state.RootInventorySha256)
        && IsSha256(state.ShareConfigurationSha256)
        && IsSha256(state.FirewallConfigurationSha256)
        && IsSha256(state.AccountConfigurationSha256)
        && IsSha256(state.SecureStoreInventorySha256)
        && IsSha256(state.MappingConfigurationSha256)
        && IsSha256(state.ServiceConfigurationSha256)
        && IsSha256(state.PolicyConfigurationSha256)
        && IsSha256(state.CombinedSha256);

    private static string PathIdentity(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));

    private static string EncodeValue(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string ParseGuestFailure(string json, string fallback)
    {
        try
        {
            var failure = ConformanceReceiptParser.Parse<GuestFailureReceipt>(json, fallback);
            return failure.Schema == "balls-windows-host-failure-v1"
                && failure.Operation == Operation
                && failure.Outcome == "failed"
                && failure.Code.Length is > 0 and <= 80
                && failure.Code.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
                    ? $"guest_{failure.Code}"
                    : fallback;
        }
        catch (ConformanceRefusalException)
        {
            return fallback;
        }
    }

    private ConformanceProcessRequest SshRequest(
        WindowsConformanceSshTransport transport,
        string remoteCommand,
        TimeSpan timeout) =>
        new(
            "ssh",
            [
                .. CommonTransportArguments(transport),
                "-p", transport.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-T", $"{transport.User}@{transport.Host}", remoteCommand,
            ],
            timeout,
            MaximumOutputBytes,
            FrameGuestScript(guestScript));

    private static ConformanceProcessRequest ScpRequest(
        WindowsConformanceSshTransport transport,
        string packagePath,
        string remotePackageName) =>
        new(
            "scp",
            [
                .. CommonTransportArguments(transport),
                "-q", "-B", "-P",
                transport.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                packagePath,
                $"{transport.User}@{transport.Host}:{remotePackageName}",
            ],
            TimeSpan.FromMinutes(2),
            8192);

    private static IEnumerable<string> CommonTransportArguments(WindowsConformanceSshTransport transport) =>
    [
        "-F", "/dev/null",
        "-o", "BatchMode=yes",
        "-o", "ClearAllForwardings=yes",
        "-o", "StrictHostKeyChecking=yes",
        "-o", $"UserKnownHostsFile={transport.KnownHostsFile}",
        "-o", "IdentitiesOnly=yes",
        "-o", $"IdentityFile={transport.PublicKeyFile}",
        "-o", "ConnectTimeout=10",
        "-o", "ConnectionAttempts=1",
        "-o", "ServerAliveInterval=5",
        "-o", "ServerAliveCountMax=2",
        "-o", "LogLevel=ERROR",
    ];

    private static string RemoteCommand(string mode, IReadOnlyDictionary<string, string> values)
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
            assignments.Append("set ").Append(value.Key).Append('=').Append(value.Value).Append("&&");
        }
        assignments.Append(
            "powershell.exe -NoLogo -NoProfile -NonInteractive " +
            "-Command \"$lines=[Collections.Generic.List[string]]::new();" +
            "while(($line=[Console]::In.ReadLine())-ne'" + ScriptEndMarker + "'){" +
            "if($null-eq$line){exit 97};$lines.Add($line)};" +
            "&([scriptblock]::Create([string]::Join([Environment]::NewLine,$lines)))\"");
        return assignments.ToString();
    }

    private static string FrameGuestScript(string script)
    {
        if (script.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Contains(ScriptEndMarker, StringComparer.Ordinal))
        {
            throw new ConformanceRefusalException("guest_script_invalid");
        }
        return script.TrimEnd('\r', '\n') + Environment.NewLine + ScriptEndMarker + Environment.NewLine;
    }

    private static void EnsureReceiptPathSafe(string receiptPath)
    {
        var path = Path.GetFullPath(receiptPath);
        if (!Directory.Exists(Path.GetDirectoryName(path)) || File.Exists(path))
        {
            throw new ConformanceRefusalException("receipt_path_unsafe");
        }
    }

    private static void WriteReceipt(string receiptPath, WindowsCircleFilesHostConformanceReceipt receipt)
    {
        if (receipt.Interventions.Count != 0)
        {
            throw new ConformanceRefusalException("intervention_contract_mismatch");
        }
        WriteJsonReceipt(receiptPath, receipt);
    }

    private static void WriteStorageReceipt(
        string receiptPath,
        WindowsCircleFilesHostStorageInspectionReceipt receipt)
    {
        if (receipt.Interventions.Count != 0)
        {
            throw new ConformanceRefusalException("intervention_contract_mismatch");
        }
        WriteJsonReceipt(receiptPath, receipt);
    }

    private static void WriteJsonReceipt<T>(string receiptPath, T receipt)
    {
        var path = Path.GetFullPath(receiptPath);
        var temporary = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(receipt, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }) + Environment.NewLine,
                new UTF8Encoding(false));
            File.Move(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
