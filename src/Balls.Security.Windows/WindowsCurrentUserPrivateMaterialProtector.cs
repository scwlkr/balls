using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Balls.Core;

namespace Balls.Security.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsCurrentUserPrivateMaterialProtector : IPrivateMaterialProtector
{
    private static readonly byte[] Entropy =
        SHA256.HashData(Encoding.UTF8.GetBytes("balls/private-material/v1"));

    public string Scheme => "windows-dpapi-current-user-v1";

    public byte[] Protect(ReadOnlySpan<byte> privateMaterial)
    {
        if (privateMaterial.IsEmpty)
        {
            throw new ArgumentException("Private material cannot be empty.", nameof(privateMaterial));
        }

        return ProtectedData.Protect(privateMaterial, DataProtectionScope.CurrentUser, Entropy);
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedMaterial)
    {
        if (protectedMaterial.IsEmpty)
        {
            throw new CryptographicException("Protected private material is empty.");
        }

        return ProtectedData.Unprotect(protectedMaterial, DataProtectionScope.CurrentUser, Entropy);
    }
}
