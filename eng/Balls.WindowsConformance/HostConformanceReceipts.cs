namespace Balls.WindowsConformance;

internal sealed record GuestHostProductIdentity(
    string Commit,
    string PackageSha256,
    string PackageName,
    string Version,
    string CliVersion,
    string DaemonVersion,
    string DaemonPrivilege,
    string BuildConfiguration);

internal sealed record GuestHostContext(
    string CircleId,
    string ContributionId,
    string FolderPath,
    string PlanId,
    string ShareName,
    string FirewallRuleName,
    string OwnershipId);

internal sealed record GuestSeedObservation(
    string FileName,
    long Length,
    string Sha256);

internal sealed record GuestHostPrepareReceipt(
    string Schema,
    string Operation,
    string Outcome,
    GuestPreflightReceipt Preflight,
    GuestHostProductIdentity Product,
    GuestHostContext Context,
    GuestSeedObservation Seed);

internal sealed record GuestHostRefusalReceipt(
    string Schema,
    string Operation,
    string Outcome,
    string PlanMismatchCode,
    string InjectedFailureCode);

internal sealed record GuestHostApplyReceipt(
    string Schema,
    string Operation,
    string Outcome,
    string ApplyStatus,
    string RetryStatus,
    string PlanId);

internal sealed record GuestHostRemovalReceipt(
    string Schema,
    string Operation,
    string Outcome,
    string RemovalStatus,
    int OpenSessionCount,
    string PlanId,
    GuestCleanupObservation Cleanup);

internal sealed record GuestHostCleanupReceipt(
    string Schema,
    string Operation,
    string Outcome,
    bool ProductRemovalAttempted,
    bool ProductResourcesRemoved,
    GuestCleanupObservation Cleanup,
    string Code);

internal sealed record GuestHostNativeObservation(
    string State,
    string PathIdentitySha256,
    bool FolderExists,
    bool FolderReparsePoint,
    GuestSeedObservation Seed,
    bool AclProtected,
    string AclSha256,
    string OwnerSidSha256,
    bool OwnerFullControl,
    bool SystemFullControl,
    bool MarkerExists,
    bool MarkerMatches,
    bool JournalExists,
    bool JournalMatches,
    bool FirewallRecoveryExists,
    int ShareCount,
    bool SharePathMatches,
    bool ShareEncryptionRequired,
    int ShareAccessCount,
    bool ShareAccessRestrictedToOwner,
    int FirewallRuleCount,
    bool FirewallPrivateOnly,
    bool FirewallLocalSubnetOnly,
    bool FirewallTcp445Only,
    bool FirewallLanmanServerOnly,
    string UnrelatedInfrastructureSha256);

internal sealed record GuestHostNativeReceipt(
    string Schema,
    string Operation,
    string Outcome,
    GuestHostNativeObservation Observation);

internal sealed record HostConformanceProductOutcomes(
    string PlanMismatch,
    string InjectedFailure,
    string Apply,
    string Retry,
    string Removal);

internal sealed record HostConformanceNativeEvidence(
    GuestHostNativeObservation Prepared,
    GuestHostNativeObservation RolledBack,
    GuestHostNativeObservation Provisioned,
    GuestHostNativeObservation Final,
    bool UnrelatedInfrastructureUnchanged,
    bool SeedBytesPreserved,
    bool FolderAclRestored);

internal sealed record HostConformanceCleanup(
    bool ProductRemovalAttempted,
    bool ProductResourcesRemoved,
    bool DaemonStopped,
    bool StateRemoved,
    bool PackageRemoved,
    bool Complete,
    string Code);

internal sealed record WindowsCircleFilesHostConformanceReceipt(
    string Schema,
    string Operation,
    string Outcome,
    string Code,
    string Phase,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    ConformanceSourceReceipt Source,
    ConformanceTargetReceipt? Target,
    string DisposablePath,
    GuestHostProductIdentity? Product,
    GuestHostContext? Context,
    GuestSeedObservation? Seed,
    HostConformanceProductOutcomes? ProductOutcomes,
    HostConformanceNativeEvidence? NativeEvidence,
    HostConformanceCleanup Cleanup,
    IReadOnlyList<string> Limitations);
