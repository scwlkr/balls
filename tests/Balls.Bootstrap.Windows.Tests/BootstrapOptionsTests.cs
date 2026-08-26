using Balls.Bootstrap.Windows;

namespace Balls.Bootstrap.Windows.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class BootstrapOptionsTests
{
    [TestMethod]
    public void Public_install_accepts_one_official_manifest_input()
    {
        var options = BootstrapOptionsParser.Parse(
            [
                "--manifest-uri", "https://balls.wlkrlabs.com/channels/development.json",
                "--install-root", Path.Combine(Path.GetTempPath(), "Balls"),
            ]);

        Assert.IsTrue(options.IsManifestInstall);
        Assert.IsTrue(options.OpenUi);
        Assert.IsTrue(options.CreateShortcut);
        Assert.AreEqual("balls", options.PipeName);
    }

    [TestMethod]
    public void Nested_nat_projection_accepts_only_one_private_ipv4_address()
    {
        var options = BootstrapOptionsParser.Parse(
            [
                "--manifest-uri", "https://balls.wlkrlabs.com/channels/development.json",
                "--advertised-private-address", "172.18.0.2",
            ]);

        Assert.AreEqual("172.18.0.2", options.AdvertisedPrivateAddress);
        Assert.ThrowsExactly<ArgumentException>(() => BootstrapOptionsParser.Parse(
            [
                "--manifest-uri", "https://balls.wlkrlabs.com/channels/development.json",
                "--advertised-private-address", "8.8.8.8",
            ]));
        Assert.ThrowsExactly<ArgumentException>(() => BootstrapOptionsParser.Parse(
            [
                "--manifest-uri", "https://balls.wlkrlabs.com/channels/development.json",
                "--advertised-private-address", "owner.example.test",
            ]));

        var launcher = WindowsBootstrapInstaller.RenderLauncher(
            "0.3.0-alpha.1-0123456789ab",
            "balls",
            "Owner-PC",
            options.AdvertisedPrivateAddress);
        StringAssert.Contains(
            launcher,
            "--automatic-private-listeners --advertised-private-address \"172.18.0.2\"");
        CollectionAssert.Contains(
            WindowsBootstrapInstaller.BuildDaemonArguments("C:\\Balls\\state", options).ToArray(),
            "172.18.0.2");
    }

    [TestMethod]
    public void Normal_launcher_records_the_relaunched_daemon_pid_for_future_updates()
    {
        var launcher = WindowsBootstrapInstaller.RenderLauncher(
            "0.3.0-alpha.1-0123456789ab",
            "balls",
            "Owner-PC",
            advertisedPrivateAddress: null);

        StringAssert.Contains(launcher, "set \"BALLS_PID=%BALLS_HOME%\\ballsd.pid\"");
        StringAssert.Contains(launcher, "Start-Process -FilePath $env:BALLS_DAEMON");
        StringAssert.Contains(launcher, "-PassThru");
        StringAssert.Contains(launcher, "$process.Id | Set-Content -LiteralPath $env:BALLS_PID");
    }

    [TestMethod]
    public void Offline_canary_is_explicit_and_rejects_mixed_or_unknown_inputs()
    {
        var options = BootstrapOptionsParser.Parse(
            [
                "--package-path", "package.zip",
                "--checksum-path", "package.zip.sha256",
                "--install-root", Path.Combine(Path.GetTempPath(), "Balls-Canary"),
                "--open-ui", "false",
                "--create-shortcut", "false",
            ]);

        Assert.IsFalse(options.IsManifestInstall);
        Assert.IsFalse(options.OpenUi);
        Assert.IsFalse(options.CreateShortcut);
        Assert.ThrowsExactly<ArgumentException>(() => BootstrapOptionsParser.Parse(
            [
                "--manifest-uri", "https://balls.wlkrlabs.com/channels/development.json",
                "--package-path", "package.zip",
                "--checksum-path", "package.zip.sha256",
            ]));
        Assert.ThrowsExactly<ArgumentException>(() => BootstrapOptionsParser.Parse(
            ["--unknown", "value"]));
    }
}
