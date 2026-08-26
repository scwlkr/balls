using Balls.Daemon;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class DaemonCommandTests
{
    [TestMethod]
    public async Task Unsupported_host_fails_closed_through_the_typed_platform_result()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
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

    [TestMethod]
    public async Task Public_or_hostname_admission_listener_is_rejected_before_state_is_created()
    {
        using var directory = new TemporaryDirectory();
        var dataDirectory = Path.Combine(directory.Path, "state");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DaemonCommand.RunAsync(
            [
                "--data-directory",
                dataDirectory,
                "--admission-listen",
                "8.8.8.8:443",
            ],
            output,
            error);

        Assert.AreEqual(DaemonExitCodes.UsageError, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.Contains(error.ToString(), "invalid --admission-listen");
        Assert.IsFalse(Directory.Exists(dataDirectory));
    }

    [TestMethod]
    public async Task Packaged_private_listener_mode_is_a_flag_and_requires_no_endpoint_value()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await DaemonCommand.RunAsync(
            ["--automatic-private-listeners", "unexpected"],
            output,
            error);

        Assert.AreEqual(DaemonExitCodes.UsageError, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.Contains(error.ToString(), "unknown argument 'unexpected'");
        Assert.IsFalse(error.ToString().Contains(
            "unknown argument '--automatic-private-listeners'",
            StringComparison.Ordinal));
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
