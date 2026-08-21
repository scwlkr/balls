using System.Runtime.Versioning;
using Balls.Core;
using Balls.Platform.MacOS;
using Balls.Security.MacOS;
using Balls.Storage.Sqlite;

namespace Balls.Platform.MacOS.Tests;

[TestClass]
[TestCategory("OSIntegration")]
[SupportedOSPlatform("macos")]
public sealed class MacOSProtectedIdentityStateTests
{
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [TestMethod]
    public async Task Identity_state_is_restart_stable_inside_owned_mode_restricted_storage()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("macOS protected-state verification requires macOS.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var stateDirectory = MacOSDataDirectorySecurity.Prepare(
            Path.Combine(directory.Path, "state"));
        var protector = new MacOSOwnedStatePrivateMaterialProtector();
        string firstKeyId;

        Assert.AreEqual(
            PrivateFileMode,
            File.GetUnixFileMode(Path.Combine(stateDirectory, "balls.db")));

        await using (var store = await SqliteLocalStateStore.OpenAsync(stateDirectory, protector))
        {
            var application = new CircleApplication(store, TimeProvider.System, "mac-node");
            await application.GetLocalNodeAsync();
            firstKeyId = (await store.GetNodeCryptographicIdentityAsync())!.Credential.KeyId;
        }

        MacOSDataDirectorySecurity.Prepare(stateDirectory);
        Assert.AreEqual(PrivateDirectoryMode, File.GetUnixFileMode(stateDirectory));
        Assert.AreEqual(
            PrivateFileMode,
            File.GetUnixFileMode(Path.Combine(stateDirectory, "balls.db")));

        await using var reopened = await SqliteLocalStateStore.OpenAsync(stateDirectory, protector);
        var restartedIdentity = await reopened.GetNodeCryptographicIdentityAsync();
        Assert.AreEqual(firstKeyId, restartedIdentity!.Credential.KeyId);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                GetCanonicalTempPath(),
                $"bmp-{Guid.NewGuid():N}"[..12]);
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }

        private static string GetCanonicalTempPath()
        {
            var path = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            return path.StartsWith("/var/", StringComparison.Ordinal) ? "/private" + path : path;
        }
    }
}
