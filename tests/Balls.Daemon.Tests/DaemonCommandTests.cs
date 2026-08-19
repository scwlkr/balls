using Balls.Daemon;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class DaemonCommandTests
{
    [TestMethod]
    public async Task Unsupported_host_fails_closed_through_the_typed_platform_result()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Supported hosts do not exercise this path.");
            return;
        }

        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DaemonCommand.RunAsync([], output, error);

        Assert.AreEqual(DaemonExitCodes.PlatformUnsupported, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.Contains(error.ToString(), "local host platform");
        StringAssert.Contains(error.ToString(), "is not supported yet");
    }

    [TestMethod]
    public async Task Invalid_startup_options_are_usage_errors_and_do_not_create_state()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var dataDirectory = Path.Combine(directory.Path, "state");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DaemonCommand.RunAsync(
            [
                "--data-directory",
                dataDirectory,
                "--pipe-name",
                "bad/name",
                "--node-name",
                "Alice-PC",
            ],
            output,
            error);

        Assert.AreEqual(DaemonExitCodes.UsageError, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.Contains(error.ToString(), "invalid --pipe-name");
        Assert.IsFalse(Directory.Exists(dataDirectory));
    }

    [TestMethod]
    public async Task Blank_node_name_is_rejected_before_state_is_created()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var dataDirectory = Path.Combine(directory.Path, "state");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DaemonCommand.RunAsync(
            [
                "--data-directory",
                dataDirectory,
                "--pipe-name",
                $"balls-tests-{Guid.NewGuid():N}",
                "--node-name",
                "   ",
            ],
            output,
            error);

        Assert.AreEqual(DaemonExitCodes.UsageError, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.Contains(error.ToString(), "--node-name requires a non-blank value");
        Assert.IsFalse(Directory.Exists(dataDirectory));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "balls-tests",
                Guid.NewGuid().ToString("N"));
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
