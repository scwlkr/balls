using Balls.Daemon;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class CircleFilesMemberMappingApplicationTests
{
    [TestMethod]
    public void Guided_open_prefers_P_without_reordering_other_supported_letters()
    {
        Assert.AreEqual(
            "P",
            CircleFilesMemberMappingApplication.SelectPreferredDrive(["D", "M", "P", "Q"]));
    }

    [TestMethod]
    public void Guided_open_uses_the_first_free_supported_letter_when_P_is_occupied()
    {
        Assert.AreEqual(
            "D",
            CircleFilesMemberMappingApplication.SelectPreferredDrive(["C", "invalid", "D", "M", "Q"]));
        Assert.IsNull(CircleFilesMemberMappingApplication.SelectPreferredDrive([]));
    }
}
