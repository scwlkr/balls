using System.Net;
using System.Security.Cryptography;

namespace Balls.Platform;

public static class CircleFilesHostingContract
{
    public const int Version = 1;
}

public sealed record CircleFilesHostRequest(
    string CircleId,
    string ContributionId,
    string ProviderId,
    string NodeId,
    string DisplayName,
    string FolderPath,
    string AuthorizationDigest,
    CircleFilesHostAuthorizationProof? Authorization = null);

public sealed record CircleFilesHostPublicCredential(
    string Role,
    string Algorithm,
    string KeyId,
    byte[] SubjectPublicKeyInfo);

public sealed record CircleFilesHostAuthorizationProof(
    byte[] Transcript,
    byte[] MemberSignature,
    byte[] CircleAuthoritySignature,
    CircleFilesHostPublicCredential MemberCredential,
    CircleFilesHostPublicCredential CircleAuthorityCredential);

public static class CircleFilesHostAuthorizationDigest
{
    public static string Compute(CircleFilesHostAuthorizationProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, proof.Transcript);
        Append(hash, proof.MemberSignature);
        Append(hash, proof.CircleAuthoritySignature);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, byte[] value)
    {
        hash.AppendData(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value.Length)));
        hash.AppendData(value);
    }
}

public sealed record CircleFilesHostPlan(
    int ContractVersion,
    string PlanId,
    string Provider,
    string FolderPath,
    string ShareName,
    string FirewallRuleName,
    string OwnershipId,
    bool TargetExists,
    IReadOnlyList<string> Actions);

public enum CircleFilesHostApplyStatus
{
    Applied,
    AlreadyApplied,
}

public sealed record CircleFilesHostApplyResult(
    CircleFilesHostApplyStatus Status,
    CircleFilesHostPlan Plan);

public interface ICircleFilesHostProvisioner
{
    ValueTask<CircleFilesHostPlan> PreviewAsync(
        CircleFilesHostRequest request,
        CancellationToken cancellationToken);

    ValueTask<CircleFilesHostApplyResult> ApplyAsync(
        CircleFilesHostRequest request,
        string expectedPlanId,
        CancellationToken cancellationToken);
}

public sealed class CircleFilesHostingException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class UnsupportedCircleFilesHostProvisioner : ICircleFilesHostProvisioner
{
    public ValueTask<CircleFilesHostPlan> PreviewAsync(
        CircleFilesHostRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CircleFilesHostPlan>(Unsupported(cancellationToken));

    public ValueTask<CircleFilesHostApplyResult> ApplyAsync(
        CircleFilesHostRequest request,
        string expectedPlanId,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CircleFilesHostApplyResult>(Unsupported(cancellationToken));

    private static Exception Unsupported(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new CircleFilesHostingException(
            "windows_required",
            "A dedicated Windows Circle Files host is supported only on Windows.");
    }
}
