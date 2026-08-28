using Balls.Platform.Windows;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class WindowsNamedPipeControlTests
{
    [TestMethod]
    public void Local_control_accepts_the_same_unelevated_user()
    {
        WindowsLocalControlIdentity.ValidateServerIdentity(
            "S-1-5-21-1000",
            "S-1-5-32-544",
            "S-1-5-21-1000",
            currentProcessElevated: false);
    }

    [TestMethod]
    public void Local_control_accepts_the_same_elevated_owner()
    {
        WindowsLocalControlIdentity.ValidateServerIdentity(
            "S-1-5-21-1000",
            "S-1-5-32-544",
            "S-1-5-32-544",
            currentProcessElevated: true);
    }

    [TestMethod]
    public void Local_control_rejects_a_different_pipe_owner()
    {
        Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
            WindowsLocalControlIdentity.ValidateServerIdentity(
                "S-1-5-21-1000",
                "S-1-5-32-544",
                "S-1-5-21-2000",
                currentProcessElevated: false));
    }
}
