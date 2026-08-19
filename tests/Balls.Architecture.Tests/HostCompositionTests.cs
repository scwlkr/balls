using Balls.Host;

namespace Balls.Architecture.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class HostCompositionTests
{
    [TestMethod]
    public void Unsupported_hosts_share_one_typed_fail_closed_result()
    {
        var linux = HostPlatformSelector.Select(HostOperatingSystem.Linux);
        var macOS = HostPlatformSelector.Select(HostOperatingSystem.MacOS);
        var unknown = HostPlatformSelector.Select(HostOperatingSystem.Unknown);

        Assert.IsInstanceOfType<UnsupportedHostPlatform>(linux);
        Assert.IsInstanceOfType<UnsupportedHostPlatform>(macOS);
        Assert.IsInstanceOfType<UnsupportedHostPlatform>(unknown);
        Assert.AreEqual(
            "the local host platform 'linux' is not supported yet.",
            ((UnsupportedHostPlatform)linux).Message);
    }

    [TestMethod]
    public void Windows_selection_composes_all_host_seams_and_preserves_defaults()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows host composition requires Windows.");
            return;
        }

        var selection = HostPlatformSelector.SelectCurrent();
        var supported = selection as SupportedHostPlatform;

        Assert.IsNotNull(supported);
        Assert.IsNotNull(supported.Platform.LocalState);
        Assert.IsNotNull(supported.Platform.LocalControlServer);
        Assert.IsNotNull(supported.Platform.LocalControlClient);
        Assert.AreEqual(Environment.MachineName, supported.Platform.Defaults.NodeDisplayName);
        Assert.AreEqual(
            "named pipe",
            supported.Platform.Defaults.LocalControlListenerDescription);
        Assert.AreEqual("pipe", supported.Platform.Defaults.LocalControlEndpointDescription);
        StringAssert.EndsWith(supported.Platform.Defaults.DataDirectory, Path.Combine("Balls"));
        StringAssert.StartsWith(
            supported.Platform.Defaults.LocalControlEndpoint,
            "balls-control-");
    }
}
