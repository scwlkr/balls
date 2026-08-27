using System.Net.Sockets;
using System.Runtime.Versioning;
using Balls.Platform.Linux;

namespace Balls.Platform.Linux.Tests;

[TestClass]
[TestCategory("OSIntegration")]
[SupportedOSPlatform("linux")]
public sealed class LinuxStateSecurityTests
{
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [TestMethod]
    public void State_is_marked_owned_and_limited_to_the_current_user()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Linux mode verification requires Linux.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var stateDirectory = Path.Combine(directory.Path, "state");

        var localState = LinuxHostPlatform.Create().LocalState;
        localState.Prepare(stateDirectory);
        var database = Path.Combine(stateDirectory, "balls.db");
        File.WriteAllText(database, string.Empty);
        File.SetUnixFileMode(database, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
        var listeners = Path.Combine(stateDirectory, "automatic-private-listeners-v1.json");
        localState.WriteNewPrivateFile(listeners, "{}"u8.ToArray());
        Assert.AreEqual(PrivateFileMode, File.GetUnixFileMode(listeners));
        File.SetUnixFileMode(listeners, UnixFileMode.UserRead | UnixFileMode.OtherRead);
        localState.Prepare(stateDirectory);

        Assert.AreEqual(PrivateDirectoryMode, File.GetUnixFileMode(stateDirectory));
        Assert.AreEqual(
            PrivateFileMode,
            File.GetUnixFileMode(Path.Combine(stateDirectory, ".balls-state")));
        Assert.AreEqual(PrivateFileMode, File.GetUnixFileMode(database));
        Assert.AreEqual(PrivateFileMode, File.GetUnixFileMode(listeners));
    }

    [TestMethod]
    public void Symbolic_link_and_unknown_content_are_rejected_without_modification()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Linux path verification requires Linux.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var target = Path.Combine(directory.Path, "target");
        var linkedState = Path.Combine(directory.Path, "linked-state");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(linkedState, target);

        Assert.ThrowsExactly<UnauthorizedAccessException>(
            () => LinuxDataDirectorySecurity.Prepare(linkedState));

        var unrelated = Path.Combine(directory.Path, "unrelated");
        Directory.CreateDirectory(unrelated);
        var important = Path.Combine(unrelated, "important.txt");
        File.WriteAllText(important, "keep me");

        Assert.ThrowsExactly<UnauthorizedAccessException>(
            () => LinuxDataDirectorySecurity.Prepare(unrelated));
        Assert.AreEqual("keep me", File.ReadAllText(important));
    }

    [TestMethod]
    public void Stale_owned_socket_is_removed_and_bound_socket_is_private()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Unix-domain sockets require Linux.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var endpoint = Path.Combine(directory.Path, "runtime", "control.sock");
        Directory.CreateDirectory(Path.GetDirectoryName(endpoint)!);
        using (var stale = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            stale.Bind(new UnixDomainSocketEndPoint(endpoint));
        }

        var transport = new LinuxUnixSocketControl();
        transport.PrepareEndpoint(endpoint);
        Assert.IsFalse(File.Exists(endpoint));

        using (var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            listener.Bind(new UnixDomainSocketEndPoint(endpoint));
            transport.SecureEndpoint(endpoint);
            Assert.AreEqual(PrivateFileMode, File.GetUnixFileMode(endpoint));
        }

        transport.CleanupEndpoint(endpoint);
        Assert.IsFalse(File.Exists(endpoint));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "state",
                $"balls-linux-os-{Guid.NewGuid():N}");
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
