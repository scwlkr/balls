using Balls.Core;
using Balls.Protocol.Remote.V1;

namespace Balls.Daemon;

internal static class IdentityProtocolMapping
{
    internal static PublicKeyCredential ToProtocol(PublicIdentityCredential credential) =>
        new(
            credential.Role switch
            {
                IdentityKeyRole.CircleAuthority => KeyRole.CircleAuthority,
                IdentityKeyRole.Anchor => KeyRole.Anchor,
                IdentityKeyRole.Member => KeyRole.Member,
                IdentityKeyRole.Node => KeyRole.Node,
                IdentityKeyRole.Transport => KeyRole.Transport,
                _ => throw new ArgumentOutOfRangeException(nameof(credential)),
            },
            credential.Algorithm,
            credential.KeyId,
            credential.SubjectPublicKeyInfo);

    internal static PublicIdentityCredential ToCore(PublicKeyCredential credential) =>
        new(
            credential.Role switch
            {
                KeyRole.CircleAuthority => IdentityKeyRole.CircleAuthority,
                KeyRole.Anchor => IdentityKeyRole.Anchor,
                KeyRole.Member => IdentityKeyRole.Member,
                KeyRole.Node => IdentityKeyRole.Node,
                KeyRole.Transport => IdentityKeyRole.Transport,
                _ => throw new ArgumentOutOfRangeException(nameof(credential)),
            },
            credential.Algorithm,
            credential.KeyId,
            credential.SubjectPublicKeyInfo);

    internal static DateTimeOffset ToProtocolSecond(DateTimeOffset value) =>
        DateTimeOffset.FromUnixTimeSeconds(value.ToUnixTimeSeconds());
}
