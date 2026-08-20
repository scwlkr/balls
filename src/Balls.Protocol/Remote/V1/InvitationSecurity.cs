using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Balls.Protocol.Remote.V1;

public static class InvitationSecurity
{
    public const string SingleUseInvitationAuthorization = "issue-single-use-invitations";
    private static readonly byte[] DelegationDomain =
        "balls/trusted-circle/invitation-delegation/v1\0"u8.ToArray();

    public static SignedInvitationIssuerDelegation SignDelegation(
        InvitationIssuerDelegation delegation,
        PublicKeyCredential rootCredential,
        ECDsa rootKey)
    {
        ArgumentNullException.ThrowIfNull(delegation);
        ArgumentNullException.ThrowIfNull(rootCredential);
        ArgumentNullException.ThrowIfNull(rootKey);
        var actual = RemoteIdentity.CreateCredential(KeyRole.CircleAuthority, rootKey);
        if (!CredentialsEqual(rootCredential, actual)
            || delegation.RootKeyId != rootCredential.KeyId)
        {
            throw new ArgumentException(
                "The Circle authority signing key does not match the delegation root.",
                nameof(rootKey));
        }

        return new SignedInvitationIssuerDelegation(
            delegation,
            RemoteSecurityProtocol.SignatureSuite,
            RemoteIdentity.Sign(EncodeDelegation(delegation), rootKey));
    }

    public static byte[] EncodeDelegation(InvitationIssuerDelegation delegation)
    {
        ArgumentNullException.ThrowIfNull(delegation);
        using var stream = new MemoryStream();
        stream.Write(DelegationDomain);
        WriteInt32(stream, RemoteSecurityProtocol.Version);
        WriteText(stream, delegation.CircleId);
        WriteInt64(stream, delegation.AuthorityGeneration);
        WriteText(stream, delegation.RootKeyId);
        WriteText(stream, delegation.IssuerId);
        WriteCredential(stream, delegation.IssuerCredential);
        WriteText(stream, delegation.Authorization);
        WriteInt64(stream, delegation.NotBeforeUtc.ToUnixTimeSeconds());
        WriteInt64(stream, delegation.ExpiresAtUtc.ToUnixTimeSeconds());
        return stream.ToArray();
    }

    public static InvitationValidationResult Validate(
        CircleInvitationPackage package,
        InvitationVerificationContext context)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            if (package.Version != InvitationPackageCodec.Version)
            {
                return Rejected(InvitationRejectionCode.UnsupportedVersion);
            }

            if (!HasValidShape(package, context))
            {
                return Rejected(InvitationRejectionCode.Malformed);
            }

            if (package.IssuerDelegation.SignatureSuite
                    != RemoteSecurityProtocol.SignatureSuite
                || package.Invitation.SignatureSuite
                    != RemoteSecurityProtocol.SignatureSuite)
            {
                return Rejected(InvitationRejectionCode.UnsupportedSuite);
            }

            var delegation = package.IssuerDelegation.Delegation;
            var invitation = package.Invitation.Invitation;
            if (!CredentialsEqual(package.RootCredential, context.TrustedRootCredential)
                || delegation.RootKeyId != package.RootCredential.KeyId
                || delegation.IssuerCredential.Role != KeyRole.Anchor
                || invitation.IssuerKeyId != delegation.IssuerCredential.KeyId
                || invitation.IssuerId != delegation.IssuerId)
            {
                return Rejected(InvitationRejectionCode.UnauthorizedIssuer);
            }

            if (!RemoteIdentity.Verify(
                    EncodeDelegation(delegation),
                    package.IssuerDelegation.RootSignature,
                    package.RootCredential)
                || !RemoteIdentity.Verify(
                    AdmissionSecurity.EncodeInvitation(invitation),
                    package.Invitation.IssuerSignature,
                    delegation.IssuerCredential))
            {
                return Rejected(InvitationRejectionCode.Forged);
            }

            if (context.RevokedKeyIds.Contains(package.RootCredential.KeyId)
                || context.RevokedKeyIds.Contains(delegation.IssuerCredential.KeyId)
                || context.InvitationState == InvitationUseState.Revoked)
            {
                return Rejected(InvitationRejectionCode.Revoked);
            }

            if (delegation.AuthorityGeneration < context.MinimumAuthorityGeneration)
            {
                return Rejected(InvitationRejectionCode.StaleAuthorityState);
            }

            if (delegation.CircleId != context.ExpectedCircleId
                || invitation.CircleId != context.ExpectedCircleId)
            {
                return Rejected(InvitationRejectionCode.WrongCircle);
            }

            if (context.NowUtc < delegation.NotBeforeUtc
                || context.NowUtc < invitation.NotBeforeUtc)
            {
                return Rejected(InvitationRejectionCode.NotYetValid);
            }

            if (context.NowUtc >= delegation.ExpiresAtUtc
                || context.NowUtc >= invitation.ExpiresAtUtc)
            {
                return Rejected(InvitationRejectionCode.Expired);
            }

            if (context.InvitationState == InvitationUseState.Consumed)
            {
                return Rejected(InvitationRejectionCode.Replayed);
            }

            return InvitationValidationResult.Valid();
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            CryptographicException or
            InvalidOperationException or
            OverflowException)
        {
            return Rejected(InvitationRejectionCode.Malformed);
        }
    }

    private static bool HasValidShape(
        CircleInvitationPackage package,
        InvitationVerificationContext context)
    {
        var delegation = package.IssuerDelegation.Delegation;
        var invitation = package.Invitation.Invitation;
        return IsCanonicalUuid(delegation.CircleId)
            && delegation.AuthorityGeneration > 0
            && RemoteIdentity.IsValidCredential(package.RootCredential)
            && package.RootCredential.Role == KeyRole.CircleAuthority
            && IsCanonicalUuid(delegation.IssuerId)
            && RemoteIdentity.IsValidCredential(delegation.IssuerCredential)
            && delegation.Authorization == SingleUseInvitationAuthorization
            && IsUtc(delegation.NotBeforeUtc)
            && IsUtc(delegation.ExpiresAtUtc)
            && delegation.NotBeforeUtc < delegation.ExpiresAtUtc
            && package.IssuerDelegation.RootSignature is { Length: 64 }
            && IsCanonicalUuid(invitation.CircleId)
            && IsCanonicalUuid(invitation.InvitationId)
            && IsCanonicalUuid(invitation.IssuerId)
            && IsKeyId(invitation.AnchorTransportKeyId, "transport:p256-sha256:")
            && invitation.AuthorityGeneration == delegation.AuthorityGeneration
            && invitation.NotBeforeUtc >= delegation.NotBeforeUtc
            && invitation.ExpiresAtUtc <= delegation.ExpiresAtUtc
            && invitation.NotBeforeUtc < invitation.ExpiresAtUtc
            && IsUtc(invitation.NotBeforeUtc)
            && IsUtc(invitation.ExpiresAtUtc)
            && invitation.MaximumRedemptions == 1
            && invitation.MinimumProtocolVersion == RemoteSecurityProtocol.Version
            && invitation.MaximumProtocolVersion == RemoteSecurityProtocol.Version
            && invitation.InvitationNonce is { Length: 32 }
            && package.Invitation.IssuerSignature is { Length: 64 }
            && IsCanonicalUuid(context.ExpectedCircleId)
            && RemoteIdentity.IsValidCredential(context.TrustedRootCredential)
            && context.TrustedRootCredential.Role == KeyRole.CircleAuthority
            && context.MinimumAuthorityGeneration > 0;
    }

    private static bool CredentialsEqual(
        PublicKeyCredential left,
        PublicKeyCredential right) =>
        left.Role == right.Role
        && left.Algorithm == right.Algorithm
        && left.KeyId == right.KeyId
        && CryptographicOperations.FixedTimeEquals(
            left.SubjectPublicKeyInfo,
            right.SubjectPublicKeyInfo);

    private static bool IsCanonicalUuid(string value) =>
        Guid.TryParseExact(value, "D", out var identifier)
        && identifier.ToString("D") == value;

    private static bool IsUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

    private static bool IsKeyId(string value, string prefix)
    {
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var encoded = value[prefix.Length..];
        if (encoded.Length != 43
            || encoded.Any(character => !(char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_')))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(
                encoded.Replace('-', '+').Replace('_', '/') + "=").Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static InvitationValidationResult Rejected(InvitationRejectionCode code) =>
        InvitationValidationResult.Rejected(code);

    private static void WriteCredential(Stream stream, PublicKeyCredential credential)
    {
        WriteInt32(stream, (int)credential.Role);
        WriteText(stream, credential.Algorithm);
        WriteText(stream, credential.KeyId);
        WriteBytes(stream, credential.SubjectPublicKeyInfo);
    }

    private static void WriteText(Stream stream, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0 || value.Length > 1024 || value.Any(character => character > 0x7f))
        {
            throw new ArgumentException("Signed text must be non-empty bounded ASCII.", nameof(value));
        }

        WriteBytes(stream, Encoding.ASCII.GetBytes(value));
    }

    private static void WriteBytes(Stream stream, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 16 * 1024)
        {
            throw new ArgumentException("Signed byte strings must be bounded.", nameof(value));
        }

        WriteInt32(stream, value.Length);
        stream.Write(value);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
