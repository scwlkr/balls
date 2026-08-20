using Balls.Protocol.Remote.V1;

namespace Balls.RemoteHarness;

internal sealed record HarnessConfiguration(
    string DnsName,
    string PeerDnsName,
    string Pkcs12Base64,
    string Pkcs12Password,
    SignedNodeTransportBinding LocalBinding,
    SignedNodeTransportBinding PeerBinding,
    PublicKeyCredential TrustedRootCredential);

internal sealed record HarnessResult(
    string Status,
    string Provider,
    string CircleId,
    string PeerNodeId,
    int ProtocolVersion,
    bool Encrypted);
