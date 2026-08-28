using Balls.WindowsConformance;

namespace Balls.WindowsConformance.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class TargetProfileTests
{
    [TestMethod]
    public void Authorized_loopback_profile_selects_only_the_readiness_operation()
    {
        using var profile = TargetProfileFixture.Create();

        var result = WindowsConformanceTargetProfileLoader.Load(profile.Path);

        Assert.AreEqual("windows-smb-readiness-v1", result.Operation);
        Assert.AreEqual("127.0.0.1", result.Transport.Host);
        Assert.AreEqual(22264, result.Transport.Port);
        Assert.AreEqual("administrator", result.ExpectedAccountKind);
    }

    [TestMethod]
    public void Unauthorized_target_fails_closed()
    {
        using var profile = TargetProfileFixture.Create(authorized: false);

        var exception = Assert.ThrowsExactly<ConformanceRefusalException>(
            () => WindowsConformanceTargetProfileLoader.Load(profile.Path));

        Assert.AreEqual("target_not_authorized", exception.Code);
    }

    [TestMethod]
    public void Non_loopback_transport_fails_closed()
    {
        using var profile = TargetProfileFixture.Create(host: "192.0.2.10");

        var exception = Assert.ThrowsExactly<ConformanceRefusalException>(
            () => WindowsConformanceTargetProfileLoader.Load(profile.Path));

        Assert.AreEqual("transport_not_loopback", exception.Code);
    }

    [TestMethod]
    public void Unknown_operation_fails_closed()
    {
        using var profile = TargetProfileFixture.Create(operation: "arbitrary-command-v1");

        var exception = Assert.ThrowsExactly<ConformanceRefusalException>(
            () => WindowsConformanceTargetProfileLoader.Load(profile.Path));

        Assert.AreEqual("operation_not_allowed", exception.Code);
    }
}
