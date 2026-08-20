using System.Runtime.Versioning;
using Balls.Core;
using Balls.Platform.Linux;
using Balls.Security.Linux;
using Balls.Storage.Sqlite;

namespace Balls.Platform.Linux.Tests;

[TestClass]
[TestCategory("OSIntegration")]
[SupportedOSPlatform("linux")]
public sealed class LinuxProtectedIdentityStateTests
{
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [TestMethod]
    public async Task Identity_state_is_restart_stable_inside_owned_mode_restricted_storage()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Linux protected-state verification requires Linux.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var stateDirectory = LinuxDataDirectorySecurity.Prepare(
            Path.Combine(directory.Path, "state"));
        var protector = new LinuxOwnedStatePrivateMaterialProtector();
        string firstKeyId;

        Assert.AreEqual(
            PrivateFileMode,
            File.GetUnixFileMode(Path.Combine(stateDirectory, "balls.db")));

        await using (var store = await SqliteLocalStateStore.OpenAsync(stateDirectory, protector))
        {
            var application = new CircleApplication(store, TimeProvider.System, "linux-node");
            await application.GetLocalNodeAsync();
            firstKeyId = (await store.GetNodeCryptographicIdentityAsync())!.Credential.KeyId;
        }

        LinuxDataDirectorySecurity.Prepare(stateDirectory);
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
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "state",
                $"balls-linux-protected-state-{Guid.NewGuid():N}");
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
    }
}
