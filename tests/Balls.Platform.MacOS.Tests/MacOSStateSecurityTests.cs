using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Balls.Platform.MacOS;

namespace Balls.Platform.MacOS.Tests;

[TestClass]
[TestCategory("OSIntegration")]
[SupportedOSPlatform("macos")]
public sealed class MacOSStateSecurityTests
{
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [TestMethod]
    public void State_is_marked_owned_and_limited_to_the_current_user()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("macOS mode verification requires macOS.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var stateDirectory = Path.Combine(directory.Path, "state");

        MacOSDataDirectorySecurity.Prepare(stateDirectory);
        var database = Path.Combine(stateDirectory, "balls.db");
        File.SetUnixFileMode(
            database,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
        MacOSDataDirectorySecurity.Prepare(stateDirectory);

        Assert.AreEqual(PrivateDirectoryMode, File.GetUnixFileMode(stateDirectory));
        Assert.AreEqual(
            PrivateFileMode,
            File.GetUnixFileMode(Path.Combine(stateDirectory, ".balls-state")));
        Assert.AreEqual(PrivateFileMode, File.GetUnixFileMode(database));
    }

    [TestMethod]
    public void Symbolic_link_unknown_content_and_extended_ACL_are_rejected_without_modification()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("macOS path verification requires macOS.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var target = Path.Combine(directory.Path, "target");
        var linkedState = Path.Combine(directory.Path, "linked-state");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(linkedState, target);

        Assert.ThrowsExactly<UnauthorizedAccessException>(
            () => MacOSDataDirectorySecurity.Prepare(linkedState));

        var unrelated = Path.Combine(directory.Path, "unrelated");
        Directory.CreateDirectory(unrelated);
        var important = Path.Combine(unrelated, "important.txt");
        File.WriteAllText(important, "keep me");

        Assert.ThrowsExactly<UnauthorizedAccessException>(
            () => MacOSDataDirectorySecurity.Prepare(unrelated));
        Assert.AreEqual("keep me", File.ReadAllText(important));

        var aclState = MacOSDataDirectorySecurity.Prepare(
            Path.Combine(directory.Path, "acl-state"));
        AddReadAcl(aclState);
        Assert.ThrowsExactly<UnauthorizedAccessException>(
            () => MacOSDataDirectorySecurity.Prepare(aclState));
    }

    [TestMethod]
    public void Stale_owned_socket_is_removed_and_bound_socket_is_private()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("Unix-domain sockets require macOS.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var endpoint = Path.Combine(directory.Path, "runtime", "control.sock");
        Directory.CreateDirectory(Path.GetDirectoryName(endpoint)!);
        using (var stale = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            stale.Bind(new UnixDomainSocketEndPoint(endpoint));
        }

        var transport = new MacOSUnixSocketControl();
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

    private static void AddReadAcl(string path)
    {
        var startInfo = new ProcessStartInfo("/bin/chmod")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("+a");
        startInfo.ArgumentList.Add("everyone allow read");
        startInfo.ArgumentList.Add(path);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start chmod for the ACL fixture.");
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, process.StandardError.ReadToEnd());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                GetCanonicalTempPath(),
                $"bmo-{Guid.NewGuid():N}"[..12]);
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
