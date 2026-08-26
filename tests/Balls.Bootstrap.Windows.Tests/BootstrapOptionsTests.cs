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
