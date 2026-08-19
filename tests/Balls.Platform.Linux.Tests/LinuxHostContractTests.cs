using System.Net.Sockets;
using System.Runtime.Versioning;
using Balls.Platform.Linux;

namespace Balls.Platform.Linux.Tests;

[TestClass]
[TestCategory("Contract")]
[SupportedOSPlatform("linux")]
public sealed class LinuxHostContractTests
{
    [TestMethod]
    public void Defaults_are_absolute_XDG_compatible_and_use_a_Unix_socket()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Linux host defaults require Linux.");
            return;
        }

        var host = LinuxHostPlatform.Create();

        Assert.IsTrue(Path.IsPathFullyQualified(host.Defaults.DataDirectory));
        Assert.IsTrue(Path.IsPathFullyQualified(host.Defaults.LocalControlEndpoint));
        StringAssert.EndsWith(host.Defaults.DataDirectory, "balls");
        var xdgStateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrWhiteSpace(xdgStateHome) && Path.IsPathFullyQualified(xdgStateHome))
        {
            Assert.AreEqual(
                Path.Combine(Path.GetFullPath(xdgStateHome), "balls"),
                host.Defaults.DataDirectory);
        }
        else
        {
            StringAssert.EndsWith(
                host.Defaults.DataDirectory,
                Path.Combine(".local", "state", "balls"));
        }
        StringAssert.EndsWith(host.Defaults.LocalControlEndpoint, Path.Combine("balls", "control.sock"));
        Assert.AreEqual("Unix-domain socket", host.Defaults.LocalControlListenerDescription);
        Assert.AreEqual("socket", host.Defaults.LocalControlEndpointDescription);
    }

    [TestMethod]
    public void Relative_state_and_socket_paths_fail_closed()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Linux path validation requires Linux.");
            return;
        }

        Assert.ThrowsExactly<ArgumentException>(
            () => LinuxDataDirectorySecurity.Prepare("relative-state"));
        Assert.ThrowsExactly<ArgumentException>(
            () => new LinuxUnixSocketControl().ValidateEndpoint("relative.sock"));
    }

    [TestMethod]
    public void Overlong_socket_paths_fail_before_any_filesystem_change()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Linux socket validation requires Linux.");
            return;
        }

        var endpoint = "/tmp/" + new string('a', 108);

        Assert.ThrowsExactly<ArgumentException>(
            () => new LinuxUnixSocketControl().ValidateEndpoint(endpoint));
    }

    [TestMethod]
    public void Live_socket_is_not_removed_as_stale()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Unix-domain sockets require Linux.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var endpoint = Path.Combine(directory.Path, "runtime", "control.sock");
        Directory.CreateDirectory(Path.GetDirectoryName(endpoint)!);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(endpoint));
        listener.Listen();

        Assert.ThrowsExactly<IOException>(
            () => new LinuxUnixSocketControl().PrepareEndpoint(endpoint));
        Assert.IsTrue(File.Exists(endpoint));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "state",
                $"balls-linux-contract-{Guid.NewGuid():N}");
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
