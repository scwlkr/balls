using System.Net.Sockets;
using System.Runtime.Versioning;
using Balls.Platform.MacOS;

namespace Balls.Platform.MacOS.Tests;

[TestClass]
[TestCategory("Contract")]
[SupportedOSPlatform("macos")]
public sealed class MacOSHostContractTests
{
    [TestMethod]
    public void Defaults_use_Application_Support_and_a_short_private_Unix_socket()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("macOS host defaults require macOS.");
            return;
        }

        var host = MacOSHostPlatform.Create();

        Assert.IsTrue(Path.IsPathFullyQualified(host.Defaults.DataDirectory));
        Assert.IsTrue(Path.IsPathFullyQualified(host.Defaults.LocalControlEndpoint));
        StringAssert.EndsWith(
            host.Defaults.DataDirectory,
            Path.Combine("Library", "Application Support", "Balls"));
        var runtimeRoot = Path.GetFullPath(Path.GetTempPath());
        if (runtimeRoot.StartsWith("/var/", StringComparison.Ordinal))
        {
            runtimeRoot = "/private" + runtimeRoot;
        }

        StringAssert.StartsWith(host.Defaults.LocalControlEndpoint, runtimeRoot);
        StringAssert.EndsWith(host.Defaults.LocalControlEndpoint, Path.Combine("balls", "control.sock"));
        Assert.AreEqual("Unix-domain socket", host.Defaults.LocalControlListenerDescription);
        Assert.AreEqual("socket", host.Defaults.LocalControlEndpointDescription);
    }

    [TestMethod]
    public void Relative_state_and_socket_paths_fail_closed()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("macOS path validation requires macOS.");
            return;
        }

        Assert.ThrowsExactly<ArgumentException>(
            () => MacOSDataDirectorySecurity.Prepare("relative-state"));
        Assert.ThrowsExactly<ArgumentException>(
            () => new MacOSUnixSocketControl().ValidateEndpoint("relative.sock"));
    }

    [TestMethod]
    public void Overlong_socket_paths_fail_before_any_filesystem_change()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("macOS socket validation requires macOS.");
            return;
        }

        var endpoint = "/tmp/" + new string('a', 104);

        Assert.ThrowsExactly<ArgumentException>(
            () => new MacOSUnixSocketControl().ValidateEndpoint(endpoint));
    }

    [TestMethod]
    public void Live_socket_is_not_removed_as_stale()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("Unix-domain sockets require macOS.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var endpoint = Path.Combine(directory.Path, "runtime", "control.sock");
        Directory.CreateDirectory(Path.GetDirectoryName(endpoint)!);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(endpoint));
        listener.Listen();

        Assert.ThrowsExactly<IOException>(
            () => new MacOSUnixSocketControl().PrepareEndpoint(endpoint));
        Assert.IsTrue(File.Exists(endpoint));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                GetCanonicalTempPath(),
                $"bmc-{Guid.NewGuid():N}"[..12]);
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
