using System.Security.Cryptography;

namespace Balls.Platform;

public static class CircleFilesGrantCredentialContract
{
    public const int Version = 1;
}

public sealed record CircleFilesGrantCredentialRequest(
    CircleFilesHostRequest Host,
    string GrantId,
    string MemberId,
    string Access,
    long Generation,
    string AuthorizationDigest,
    CircleFilesHostAuthorizationProof Authorization);

public sealed record CircleFilesGrantCredentialPlan(
    int ContractVersion,
    string PlanId,
    string Provider,
    string FolderPath,
    string ShareName,
    string AccountName,
    string OwnershipId,
    string Access,
    long Generation,
    IReadOnlyList<string> Actions);

public enum CircleFilesGrantCredentialApplyStatus
{
    Applied,
    AlreadyApplied,
}

public sealed record CircleFilesGrantCredentialApplyResult(
    CircleFilesGrantCredentialApplyStatus Status,
    CircleFilesGrantCredentialPlan Plan);

public interface ICircleFilesGrantCredentialProvisioner
{
    ValueTask<CircleFilesGrantCredentialPlan> PreviewAsync(
        CircleFilesGrantCredentialRequest request,
        CancellationToken cancellationToken);

    ValueTask<CircleFilesGrantCredentialApplyResult> ApplyAsync(
        CircleFilesGrantCredentialRequest request,
        string expectedPlanId,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken);
}

public static class CircleFilesGrantSecret
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!#$%+-_=";
    private static readonly string All = Upper + Lower + Digits + Symbols;

    public static byte[] Generate()
    {
        Span<char> value = stackalloc char[32];
        value[0] = RandomCharacter(Upper);
        value[1] = RandomCharacter(Lower);
        value[2] = RandomCharacter(Digits);
        value[3] = RandomCharacter(Symbols);
        for (var index = 4; index < value.Length; index++)
        {
            value[index] = RandomCharacter(All);
        }

        for (var index = value.Length - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (value[index], value[swap]) = (value[swap], value[index]);
        }

        var encoded = new byte[System.Text.Encoding.UTF8.GetByteCount(value)];
        _ = System.Text.Encoding.UTF8.GetBytes(value, encoded);
        value.Clear();
        return encoded;
    }

    private static char RandomCharacter(string alphabet) =>
        alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}

public sealed class UnsupportedCircleFilesGrantCredentialProvisioner :
    ICircleFilesGrantCredentialProvisioner
{
    public ValueTask<CircleFilesGrantCredentialPlan> PreviewAsync(
        CircleFilesGrantCredentialRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CircleFilesGrantCredentialPlan>(Unsupported(cancellationToken));

    public ValueTask<CircleFilesGrantCredentialApplyResult> ApplyAsync(
        CircleFilesGrantCredentialRequest request,
        string expectedPlanId,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CircleFilesGrantCredentialApplyResult>(Unsupported(cancellationToken));

    private static Exception Unsupported(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new CircleFilesHostingException(
            "windows_required",
            "A Windows SMB Member credential can be issued only on Windows.");
    }
}
