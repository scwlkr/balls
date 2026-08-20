using System.Runtime.Versioning;
using System.Security.Cryptography;
using Balls.Core;

namespace Balls.Security.Linux;

[SupportedOSPlatform("linux")]
public sealed class LinuxOwnedStatePrivateMaterialProtector : IPrivateMaterialProtector
{
    public string Scheme => "linux-owned-state-v1";

    public byte[] Protect(ReadOnlySpan<byte> privateMaterial)
    {
        if (privateMaterial.IsEmpty)
        {
            throw new ArgumentException("Private material cannot be empty.", nameof(privateMaterial));
        }

        return privateMaterial.ToArray();
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedMaterial)
    {
        if (protectedMaterial.IsEmpty)
        {
            throw new CryptographicException("Protected private material is empty.");
        }

        return protectedMaterial.ToArray();
    }
}
