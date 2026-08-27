using Balls.Host;

namespace Balls.Architecture.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class HostCompositionTests
{
    [TestMethod]
    public async Task Non_Windows_readiness_is_explicitly_unknown()
    {
        var report = await new Balls.Platform.UnsupportedCircleFilesReadinessInspector()
            .InspectAsync(CancellationToken.None);

        Assert.AreEqual(Balls.Platform.CircleFilesReadinessStatus.Unknown, report.Status);
        Assert.AreEqual("windows_required", report.Checks.Single().Code);
    }

    [TestMethod]
    public void Unsupported_hosts_share_one_typed_fail_closed_result()
    {
        var unknown = HostPlatformSelector.Select(HostOperatingSystem.Unknown);

        Assert.IsInstanceOfType<UnsupportedHostPlatform>(unknown);
        Assert.AreEqual(
            "the local host platform 'unknown' is not supported yet.",
            ((UnsupportedHostPlatform)unknown).Message);
    }

    [TestMethod]
    public void MacOS_selection_composes_all_host_seams()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("macOS host composition requires macOS.");
            return;
        }

        var selection = HostPlatformSelector.SelectCurrent();
        var supported = selection as SupportedHostPlatform;

        Assert.IsNotNull(supported);
        Assert.IsNotNull(supported.Platform.LocalState);
        Assert.IsNotNull(supported.Platform.LocalControlServer);
        Assert.IsNotNull(supported.Platform.LocalControlClient);
        Assert.IsInstanceOfType<Balls.Platform.UnsupportedCircleFilesFolderPicker>(
            supported.Platform.CircleFilesFolderPicker);
        Assert.IsInstanceOfType<Balls.Platform.UnsupportedCircleFilesLocationLauncher>(
            supported.Platform.CircleFilesLocationLauncher);
        Assert.IsNotNull(supported.Platform.CircleFilesReadiness);
        Assert.IsInstanceOfType<Balls.Platform.UnsupportedRevitServerReadinessInspector>(
            supported.Platform.RevitServerReadiness);
        Assert.IsInstanceOfType<Balls.Platform.UnsupportedRevitServerMediaPicker>(
            supported.Platform.RevitServerMediaPicker);
        Assert.AreEqual("macos-owned-state-v1", supported.PrivateMaterialProtector.Scheme);
        Assert.AreEqual("Unix-domain socket", supported.Platform.Defaults.LocalControlListenerDescription);
        Assert.AreEqual("socket", supported.Platform.Defaults.LocalControlEndpointDescription);
        StringAssert.EndsWith(
            supported.Platform.Defaults.DataDirectory,
            Path.Combine("Library", "Application Support", "Balls"));
    }

    [TestMethod]
    public void Linux_selection_composes_all_host_seams()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Linux host composition requires Linux.");
            return;
        }

        var selection = HostPlatformSelector.SelectCurrent();
        var supported = selection as SupportedHostPlatform;

        Assert.IsNotNull(supported);
        Assert.IsNotNull(supported.Platform.LocalState);
        Assert.IsNotNull(supported.Platform.LocalControlServer);
        Assert.IsNotNull(supported.Platform.LocalControlClient);
        Assert.IsInstanceOfType<Balls.Platform.UnsupportedCircleFilesFolderPicker>(
            supported.Platform.CircleFilesFolderPicker);
        Assert.IsInstanceOfType<Balls.Platform.UnsupportedCircleFilesLocationLauncher>(
            supported.Platform.CircleFilesLocationLauncher);
        Assert.IsNotNull(supported.Platform.CircleFilesReadiness);
        Assert.IsInstanceOfType<Balls.Platform.UnsupportedRevitServerReadinessInspector>(
            supported.Platform.RevitServerReadiness);
        Assert.IsInstanceOfType<Balls.Platform.UnsupportedRevitServerMediaPicker>(
            supported.Platform.RevitServerMediaPicker);
        Assert.AreEqual("linux-owned-state-v1", supported.PrivateMaterialProtector.Scheme);
        Assert.AreEqual("Unix-domain socket", supported.Platform.Defaults.LocalControlListenerDescription);
        Assert.AreEqual("socket", supported.Platform.Defaults.LocalControlEndpointDescription);
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
        Assert.IsInstanceOfType<Balls.Platform.Windows.WindowsCircleFilesFolderPicker>(
            supported.Platform.CircleFilesFolderPicker);
        Assert.IsInstanceOfType<Balls.Platform.Windows.WindowsCircleFilesLocationLauncher>(
            supported.Platform.CircleFilesLocationLauncher);
        Assert.IsNotNull(supported.Platform.CircleFilesReadiness);
        Assert.IsInstanceOfType<Balls.Platform.Windows.WindowsRevitServerReadinessInspector>(
            supported.Platform.RevitServerReadiness);
        Assert.IsInstanceOfType<Balls.Platform.Windows.WindowsRevitServerMediaPicker>(
            supported.Platform.RevitServerMediaPicker);
        Assert.AreEqual("windows-dpapi-current-user-v1", supported.PrivateMaterialProtector.Scheme);
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
