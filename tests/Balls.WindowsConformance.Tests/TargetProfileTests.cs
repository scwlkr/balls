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
    public void Product_transport_must_use_the_same_host_key_endpoint()
    {
        using var profile = TargetProfileFixture.Create(productPort: 22265);

        var exception = Assert.ThrowsExactly<ConformanceRefusalException>(
            () => WindowsConformanceTargetProfileLoader.Load(profile.Path));

        Assert.AreEqual("transport_target_mismatch", exception.Code);
    }

    [TestMethod]
    public void Unknown_operation_fails_closed()
    {
        using var profile = TargetProfileFixture.Create(operation: "arbitrary-command-v1");

        var exception = Assert.ThrowsExactly<ConformanceRefusalException>(
            () => WindowsConformanceTargetProfileLoader.Load(profile.Path));

        Assert.AreEqual("operation_not_allowed", exception.Code);
    }

    [TestMethod]
    public void Host_operation_requires_an_exact_authorized_disposable_path()
    {
        var expectedVolumeIdentity = new string('b', 64);
        var expectedDiskIdentity = new string('c', 64);
        using var profile = TargetProfileFixture.Create(
            operation: "windows-circle-files-host-v1",
            disposablePath: @"C:\BallsConformance\Issue124-clean-a",
            expectedVolumeIdentitySha256: expectedVolumeIdentity,
            expectedDiskIdentitySha256: expectedDiskIdentity);

        var result = WindowsConformanceTargetProfileLoader.Load(profile.Path);

        Assert.AreEqual(@"C:\BallsConformance\Issue124-clean-a", result.DisposablePath);
        Assert.AreEqual(expectedVolumeIdentity, result.ExpectedVolumeIdentitySha256);
        Assert.AreEqual(expectedDiskIdentity, result.ExpectedDiskIdentitySha256);
    }

    [TestMethod]
    public void Host_operation_requires_exact_volume_and_disk_identities()
    {
        using var profile = TargetProfileFixture.Create(
            operation: "windows-circle-files-host-v1",
            disposablePath: @"C:\BallsConformance\Issue124-clean-a",
            expectedVolumeIdentitySha256: "missing",
            expectedDiskIdentitySha256: new string('c', 64));

        var exception = Assert.ThrowsExactly<ConformanceRefusalException>(
            () => WindowsConformanceTargetProfileLoader.Load(profile.Path));

        Assert.AreEqual("disposable_storage_not_authorized", exception.Code);
    }

    [TestMethod]
    [DataRow(@"C:\BallsDemo\Projects")]
    [DataRow(@"C:\BallsConformance\Issue123-old")]
    [DataRow(@"Z:\BallsConformance\Issue124-host.vhdx")]
    [DataRow(@"\\server\share\Issue124-clean")]
    public void Host_operation_refuses_production_ambiguous_or_network_paths(string path)
    {
        using var profile = TargetProfileFixture.Create(
            operation: "windows-circle-files-host-v1",
            disposablePath: path);

        var exception = Assert.ThrowsExactly<ConformanceRefusalException>(
            () => WindowsConformanceTargetProfileLoader.Load(profile.Path));

        Assert.AreEqual("disposable_path_not_authorized", exception.Code);
    }
}
