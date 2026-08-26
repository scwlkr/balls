using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Balls.Platform;
using Balls.Platform.Windows;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("Contract")]
[SupportedOSPlatform("windows")]
public sealed class WindowsCircleFilesLocationLauncherTests
{
    [TestMethod]
    public async Task Opens_the_exact_drive_root_in_the_current_process_context_without_elevation()
    {
        var process = new StubProcess();
        var launcher = new WindowsCircleFilesLocationLauncher(process);

        await launcher.OpenAsync(new CircleFilesMappedLocation("p"), CancellationToken.None);

        Assert.IsNotNull(process.StartInfo);
        Assert.AreEqual("explorer.exe", process.StartInfo.FileName);
        Assert.IsFalse(process.StartInfo.UseShellExecute);
        Assert.IsTrue(process.StartInfo.CreateNoWindow);
        Assert.AreEqual(string.Empty, process.StartInfo.Verb);
        CollectionAssert.AreEqual(new[] { @"P:\" }, process.StartInfo.ArgumentList.ToArray());
    }

    [TestMethod]
    public async Task Start_failure_is_bounded_and_typed()
    {
        var process = new StubProcess { Failure = new Win32Exception(5, "secret native detail") };
        var launcher = new WindowsCircleFilesLocationLauncher(process);

        var error = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => launcher.OpenAsync(
                new CircleFilesMappedLocation("P"),
                CancellationToken.None).AsTask());

        Assert.AreEqual("explorer_launch_failed", error.Code);
        Assert.AreEqual(
            "The shared folder is connected, but File Explorer did not open. Try again.",
            error.Message);
        Assert.IsFalse(error.Message.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Invalid_drive_is_rejected_before_start()
    {
        var process = new StubProcess();
        var launcher = new WindowsCircleFilesLocationLauncher(process);

        var error = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => launcher.OpenAsync(
                new CircleFilesMappedLocation("C"),
                CancellationToken.None).AsTask());

        Assert.AreEqual("mapping_request_invalid", error.Code);
        Assert.IsNull(process.StartInfo);
    }

    private sealed class StubProcess : IWindowsCircleFilesLocationProcess
    {
        internal ProcessStartInfo? StartInfo { get; private set; }
        internal Exception? Failure { get; init; }

        public bool Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            if (Failure is not null)
            {
                throw Failure;
            }
            return true;
        }
    }
}
