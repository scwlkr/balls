using System.Globalization;
using System.Text.Json;

namespace Balls.Protocol.Remote.V1;

public sealed class InvitationPackageException()
    : Exception("The Circle invitation package is malformed or noncanonical.");

public static class InvitationPackageCodec
{
    public const int Version = 1;
    public const int MaximumEncodedLength = 16 * 1024;
    private const string Format = "balls-circle-invitation";

    public static byte[] Encode(CircleInvitationPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteString("format", Format);
            writer.WriteNumber("version", package.Version);
            WriteCredential(writer, "rootCredential", package.RootCredential);
            WriteDelegation(writer, package.IssuerDelegation);
            WriteInvitation(writer, package.Invitation);
            writer.WriteEndObject();
        }

        var result = output.ToArray();
        if (result.Length > MaximumEncodedLength)
        {
            throw new InvitationPackageException();
        }

        return result;
    }

    public static CircleInvitationPackage Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length is 0 or > MaximumEncodedLength)
        {
            throw new InvitationPackageException();
        }

        try
        {
            using var document = JsonDocument.Parse(
                encoded.ToArray(),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 8,
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.GetProperty("format").GetString() != Format)
            {
                throw new InvitationPackageException();
            }

            var package = new CircleInvitationPackage(
                root.GetProperty("version").GetInt32(),
                ReadCredential(root.GetProperty("rootCredential")),
                ReadDelegation(root.GetProperty("issuerDelegation")),
                ReadInvitation(root.GetProperty("invitation")));
            if (!encoded.SequenceEqual(Encode(package)))
            {
                throw new InvitationPackageException();
            }

            return package;
        }
        catch (InvitationPackageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            FormatException or
            InvalidOperationException or
            JsonException or
            KeyNotFoundException or
            OverflowException)
        {
            throw new InvitationPackageException();
        }
    }

    private static void WriteDelegation(
        Utf8JsonWriter writer,
        SignedInvitationIssuerDelegation signedDelegation)
    {
        var delegation = signedDelegation.Delegation;
        writer.WriteStartObject("issuerDelegation");
        writer.WriteString("circleId", delegation.CircleId);
        writer.WriteNumber("authorityGeneration", delegation.AuthorityGeneration);
        writer.WriteString("rootKeyId", delegation.RootKeyId);
        writer.WriteString("issuerId", delegation.IssuerId);
        WriteCredential(writer, "issuerCredential", delegation.IssuerCredential);
        writer.WriteString("authorization", delegation.Authorization);
        writer.WriteString("notBeforeUtc", FormatTimestamp(delegation.NotBeforeUtc));
        writer.WriteString("expiresAtUtc", FormatTimestamp(delegation.ExpiresAtUtc));
        writer.WriteString("signatureSuite", signedDelegation.SignatureSuite);
        writer.WriteBase64String("rootSignature", signedDelegation.RootSignature);
        writer.WriteEndObject();
    }

    private static void WriteInvitation(
        Utf8JsonWriter writer,
        SignedCircleInvitation signedInvitation)
    {
        var invitation = signedInvitation.Invitation;
        writer.WriteStartObject("invitation");
        writer.WriteString("circleId", invitation.CircleId);
        writer.WriteString("invitationId", invitation.InvitationId);
        writer.WriteString("issuerId", invitation.IssuerId);
        writer.WriteString("issuerKeyId", invitation.IssuerKeyId);
        writer.WriteString("anchorTransportKeyId", invitation.AnchorTransportKeyId);
        writer.WriteNumber("authorityGeneration", invitation.AuthorityGeneration);
        writer.WriteString("notBeforeUtc", FormatTimestamp(invitation.NotBeforeUtc));
        writer.WriteString("expiresAtUtc", FormatTimestamp(invitation.ExpiresAtUtc));
        writer.WriteNumber("maximumRedemptions", invitation.MaximumRedemptions);
        writer.WriteNumber("minimumProtocolVersion", invitation.MinimumProtocolVersion);
        writer.WriteNumber("maximumProtocolVersion", invitation.MaximumProtocolVersion);
        writer.WriteBase64String("invitationNonce", invitation.InvitationNonce);
        writer.WriteString("signatureSuite", signedInvitation.SignatureSuite);
        writer.WriteBase64String("issuerSignature", signedInvitation.IssuerSignature);
        writer.WriteEndObject();
    }

    private static void WriteCredential(
        Utf8JsonWriter writer,
        string propertyName,
        PublicKeyCredential credential)
    {
        writer.WriteStartObject(propertyName);
        writer.WriteString("role", RoleName(credential.Role));
        writer.WriteString("algorithm", credential.Algorithm);
        writer.WriteString("keyId", credential.KeyId);
        writer.WriteBase64String("subjectPublicKeyInfo", credential.SubjectPublicKeyInfo);
        writer.WriteEndObject();
    }

    private static SignedInvitationIssuerDelegation ReadDelegation(JsonElement element)
    {
        EnsureObject(element);
        return new SignedInvitationIssuerDelegation(
            new InvitationIssuerDelegation(
                element.GetProperty("circleId").GetString()!,
                element.GetProperty("authorityGeneration").GetInt64(),
                element.GetProperty("rootKeyId").GetString()!,
                element.GetProperty("issuerId").GetString()!,
                ReadCredential(element.GetProperty("issuerCredential")),
                element.GetProperty("authorization").GetString()!,
                ReadTimestamp(element.GetProperty("notBeforeUtc")),
                ReadTimestamp(element.GetProperty("expiresAtUtc"))),
            element.GetProperty("signatureSuite").GetString()!,
            element.GetProperty("rootSignature").GetBytesFromBase64());
    }

    private static SignedCircleInvitation ReadInvitation(JsonElement element)
    {
        EnsureObject(element);
        return new SignedCircleInvitation(
            new CircleInvitation(
                element.GetProperty("circleId").GetString()!,
                element.GetProperty("invitationId").GetString()!,
                element.GetProperty("issuerId").GetString()!,
                element.GetProperty("issuerKeyId").GetString()!,
                element.GetProperty("anchorTransportKeyId").GetString()!,
                element.GetProperty("authorityGeneration").GetInt64(),
                ReadTimestamp(element.GetProperty("notBeforeUtc")),
                ReadTimestamp(element.GetProperty("expiresAtUtc")),
                element.GetProperty("maximumRedemptions").GetInt32(),
                element.GetProperty("minimumProtocolVersion").GetInt32(),
                element.GetProperty("maximumProtocolVersion").GetInt32(),
                element.GetProperty("invitationNonce").GetBytesFromBase64()),
            element.GetProperty("signatureSuite").GetString()!,
            element.GetProperty("issuerSignature").GetBytesFromBase64());
    }

    private static PublicKeyCredential ReadCredential(JsonElement element)
    {
        EnsureObject(element);
        return new PublicKeyCredential(
            ParseRole(element.GetProperty("role").GetString()),
            element.GetProperty("algorithm").GetString()!,
            element.GetProperty("keyId").GetString()!,
            element.GetProperty("subjectPublicKeyInfo").GetBytesFromBase64());
    }

    private static DateTimeOffset ReadTimestamp(JsonElement element)
    {
        if (!DateTimeOffset.TryParseExact(
                element.GetString(),
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var value))
        {
            throw new InvitationPackageException();
        }

        return value;
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static void EnsureObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvitationPackageException();
        }
    }

    private static string RoleName(KeyRole role) => role switch
    {
        KeyRole.CircleAuthority => "circle-authority",
        KeyRole.Anchor => "anchor",
        KeyRole.Member => "member",
        KeyRole.Node => "node",
        KeyRole.Transport => "transport",
        _ => throw new InvitationPackageException(),
    };

    private static KeyRole ParseRole(string? role) => role switch
    {
        "circle-authority" => KeyRole.CircleAuthority,
        "anchor" => KeyRole.Anchor,
        "member" => KeyRole.Member,
        "node" => KeyRole.Node,
        "transport" => KeyRole.Transport,
        _ => throw new InvitationPackageException(),
    };
}
