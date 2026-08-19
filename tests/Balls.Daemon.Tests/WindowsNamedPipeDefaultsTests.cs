using Balls.Platform.Windows;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("OSIntegration")]
public sealed class WindowsNamedPipeDefaultsTests
{
    [TestMethod]
    public void Local_control_client_caps_buffered_response_content()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        using var client = WindowsNamedPipeHttpClient.Create(
            $"balls-tests-{Guid.NewGuid():N}");

        Assert.AreEqual(256 * 1024, client.MaxResponseContentBufferSize);
    }

    [TestMethod]
    public void Default_pipe_name_is_stable_and_contains_no_account_identity_text()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 local control transport is currently Windows-only.");
            return;
        }

        var first = WindowsNamedPipeDefaults.GetCurrentUserPipeName();
        var second = WindowsNamedPipeDefaults.GetCurrentUserPipeName();

        Assert.AreEqual(first, second);
        StringAssert.Matches(first, new System.Text.RegularExpressions.Regex("^balls-control-[0-9a-f]{16}$"));
        Assert.IsFalse(first.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
    }
}
