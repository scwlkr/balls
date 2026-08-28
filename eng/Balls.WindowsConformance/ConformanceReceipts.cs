using System.Text.Json;
using System.Text.Json.Serialization;

namespace Balls.WindowsConformance;

internal sealed record GuestAccountObservation(
    string Kind,
    bool Elevated,
    string Integrity,
    string IdentitySha256);

internal sealed record GuestWindowsObservation(
    string ProductName,
    string DisplayVersion,
    string BuildNumber,
    string InstallationType);

internal sealed record GuestPolicyObservation(
    string ExecutionPolicy,
    bool UacEnabled,
    string ApplicationControl);

internal sealed record GuestNetworkObservation(
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> FirewallProfiles);

internal sealed record GuestDirtyStateObservation(
    int ExistingBallsProcesses,
    int OwnedArtifacts,
    bool Clean);

internal sealed record GuestPreflightReceipt(
    string Schema,
    string Operation,
    string Outcome,
    string ComputerName,
    GuestAccountObservation Account,
    GuestWindowsObservation Windows,
    GuestPolicyObservation Policy,
    GuestNetworkObservation Network,
    GuestDirtyStateObservation DirtyState);

internal sealed record GuestProductIdentity(
    string Commit,
    string PackageSha256,
    string PackageName,
    string Version,
    string CliVersion,
    string DaemonVersion,
    string DaemonPrivilege);

internal sealed record GuestReadinessCheck(
    string Id,
    string Status,
    string Code,
    string Summary);

internal sealed record GuestProductReadiness(
    string Provider,
    string Status,
    IReadOnlyList<GuestReadinessCheck> Checks);

internal sealed record GuestNativeObservation(
    bool ServerSmb2Enabled,
    bool ServerSigningRequired,
    bool ServerEncryptionSupported,
    bool ServerRejectsUnencryptedAccess,
    bool ClientSigningRequired,
    bool ClientEncryptionRequired,
    bool InsecureGuestLogonsEnabled,
    string ServerSmb1FeatureState,
    string ClientSmb1FeatureState,
    IReadOnlyList<string> NetworkCategories,
    IReadOnlyList<string> FirewallProfiles,
    int PublicSmbAllowRules,
    int PublicSmbBlockRules);

internal sealed record GuestNativeReceipt(
    string Schema,
    string Operation,
    string Outcome,
    GuestNativeObservation Observation);

internal sealed record GuestCleanupObservation(
    bool DaemonStopped,
    bool StateRemoved,
    bool PackageRemoved,
    bool Complete);

internal sealed record GuestFailureReceipt(
    string Schema,
    string Operation,
    string Outcome,
    string Code);

internal sealed record GuestRunReceipt(
    string Schema,
    string Operation,
    string Outcome,
    GuestPreflightReceipt Preflight,
    GuestProductIdentity Product,
    GuestProductReadiness ProductReadiness,
    GuestCleanupObservation Cleanup,
    IReadOnlyList<string> Limitations);

internal sealed record ConformanceSourceReceipt(
    string Commit,
    string PackageName,
    string PackageSha256,
    string Version,
    string Architecture,
    string DaemonPrivilege);

internal sealed record ConformanceTargetReceipt(
    string TargetId,
    string ConnectivityPath,
    GuestPreflightReceipt InspectionPreflight,
    GuestPreflightReceipt ProductPreflight);

internal sealed record WindowsSmbReadinessConformanceReceipt(
    string Schema,
    string Operation,
    string Outcome,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    ConformanceSourceReceipt Source,
    ConformanceTargetReceipt Target,
    GuestProductReadiness ProductReadiness,
    GuestNativeObservation NativeObservation,
    bool NativeStateUnchanged,
    GuestCleanupObservation Cleanup,
    IReadOnlyList<string> Limitations);

internal static class ConformanceReceiptParser
{
    private const int MaximumReceiptBytes = 64 * 1024;
    private static readonly HashSet<string> ForbiddenPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "credential",
        "credentials",
        "dpapi",
        "invitation",
        "password",
        "privateKey",
        "providerSecret",
        "secret",
        "token",
    };

    public static T Parse<T>(string json, string invalidCode)
    {
        if (string.IsNullOrWhiteSpace(json)
            || JsonSerializer.SerializeToUtf8Bytes(json).Length > MaximumReceiptBytes)
        {
            throw new ConformanceRefusalException(invalidCode);
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            RejectForbiddenProperties(document.RootElement);
            return JsonSerializer.Deserialize<T>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        RespectNullableAnnotations = true,
                        RespectRequiredConstructorParameters = true,
                        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                    })
                ?? throw new ConformanceRefusalException(invalidCode);
        }
        catch (JsonException)
        {
            throw new ConformanceRefusalException(invalidCode);
        }
    }

    private static void RejectForbiddenProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (ForbiddenPropertyNames.Contains(property.Name))
                {
                    throw new ConformanceRefusalException("receipt_contains_secret");
                }

                RejectForbiddenProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                RejectForbiddenProperties(item);
            }
        }
    }
}
