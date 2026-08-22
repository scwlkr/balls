using System.Security.Cryptography;

namespace Balls.Core;

public sealed record CircleFilesProviderCredentialBinding(
    string GrantId,
    string CircleId,
    string ContributionId,
    string MemberId,
    string Provider,
    string AccountName,
    string OwnershipId,
    string Access,
    long Generation);

public sealed class CircleFilesProviderCredentialMaterial : IDisposable
{
    private byte[] secret;

    public CircleFilesProviderCredentialMaterial(
        CircleFilesProviderCredentialBinding binding,
        byte[] secret,
        bool isNew,
        bool isActive)
    {
        Binding = binding;
        this.secret = secret;
        IsNew = isNew;
        IsActive = isActive;
    }

    public CircleFilesProviderCredentialBinding Binding { get; }

    public bool IsNew { get; }

    public bool IsActive { get; }

    public ReadOnlyMemory<byte> Secret => secret;

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(secret);
        secret = [];
    }

    public override string ToString() => "Circle Files provider credential (redacted)";
}

public interface ICircleFilesProviderCredentialStore
{
    Task<CircleFilesProviderCredentialMaterial?> GetActiveCircleFilesProviderCredentialAsync(
        string grantId,
        CancellationToken cancellationToken = default);

    Task<CircleFilesProviderCredentialMaterial> PrepareCircleFilesProviderCredentialAsync(
        CircleFilesProviderCredentialBinding binding,
        ReadOnlyMemory<byte> candidateSecret,
        CancellationToken cancellationToken = default);

    Task CompleteCircleFilesProviderCredentialAsync(
        CircleFilesProviderCredentialBinding binding,
        CancellationToken cancellationToken = default);
}
