using System.Runtime.Versioning;
using System.Security.Cryptography;
using Balls.Core;
using Balls.Platform.Windows;
using Balls.Security.Windows;
using Balls.Storage.Sqlite;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("OSIntegration")]
[SupportedOSPlatform("windows")]
public sealed class WindowsPrivateMaterialProtectionTests
{
    [TestMethod]
    public async Task Dpapi_state_is_restart_stable_and_rejects_substitution()
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

        var root = Path.Combine(
            Path.GetTempPath(),
            "balls-windows-protected-state",
            Guid.NewGuid().ToString("N"));
        try
        {
            var stateDirectory = WindowsDataDirectorySecurity.Prepare(root);
            string firstKeyId;
            await using (var store = await SqliteLocalStateStore.OpenAsync(
                             stateDirectory,
                             protector))
            {
                var application = new CircleApplication(store, TimeProvider.System, "windows-node");
                await application.GetLocalNodeAsync();
                var identity = (await store.GetNodeCryptographicIdentityAsync())!;
                firstKeyId = identity.Credential.KeyId;
                var signature = await store.SignWithNodeAsync("windows-dpapi-proof"u8.ToArray());
                Assert.IsTrue(IdentityCryptography.Verify(
                    "windows-dpapi-proof"u8,
                    signature,
                    identity.Credential));
            }

            WindowsDataDirectorySecurity.Prepare(stateDirectory);
            await using var restarted = await SqliteLocalStateStore.OpenAsync(
                stateDirectory,
                protector);
            Assert.AreEqual(
                firstKeyId,
                (await restarted.GetNodeCryptographicIdentityAsync())!.Credential.KeyId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
