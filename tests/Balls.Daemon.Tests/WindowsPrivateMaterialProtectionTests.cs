using System.Runtime.Versioning;
using System.Security.Cryptography;
using Balls.Security.Windows;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("OSIntegration")]
[SupportedOSPlatform("windows")]
public sealed class WindowsPrivateMaterialProtectionTests
{
    [TestMethod]
    public void Dpapi_current_user_round_trips_without_storing_plaintext_and_rejects_substitution()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows DPAPI verification requires Windows.");
            return;
        }

        var protector = new WindowsCurrentUserPrivateMaterialProtector();
        var privateMaterial = RandomNumberGenerator.GetBytes(138);
        var protectedMaterial = protector.Protect(privateMaterial);

        CollectionAssert.AreNotEqual(privateMaterial, protectedMaterial);
        CollectionAssert.AreEqual(privateMaterial, protector.Unprotect(protectedMaterial));

        protectedMaterial[protectedMaterial.Length / 2] ^= 0x80;
        Assert.ThrowsExactly<CryptographicException>(() => protector.Unprotect(protectedMaterial));

        CryptographicOperations.ZeroMemory(privateMaterial);
    }
}
