using Balls.WindowsConformance;

namespace Balls.WindowsConformance.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class ConformanceProcessTests
{
    [TestMethod]
    public async Task Missing_transport_executable_returns_a_stable_refusal()
    {
        var runner = new SystemConformanceProcessRunner();

        var exception = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            runner.RunAsync(
                new ConformanceProcessRequest(
                    $"balls-conformance-missing-{Guid.NewGuid():N}",
                    [],
                    TimeSpan.FromSeconds(1),
                    1024),
                CancellationToken.None));

        Assert.AreEqual("transport_start_failed", exception.Code);
    }

    [TestMethod]
    public async Task Transport_timeout_terminates_the_process_with_a_stable_refusal()
    {
        var runner = new SystemConformanceProcessRunner();

        var exception = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            runner.RunAsync(
                new ConformanceProcessRequest(
                    "/usr/bin/sleep",
                    ["10"],
                    TimeSpan.FromMilliseconds(100),
                    1024),
                CancellationToken.None));

        Assert.AreEqual("transport_timeout", exception.Code);
    }

    [TestMethod]
    public async Task Oversized_transport_output_returns_a_stable_refusal()
    {
        var runner = new SystemConformanceProcessRunner();

        var exception = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            runner.RunAsync(
                new ConformanceProcessRequest(
                    "/usr/bin/printf",
                    [new string('x', 128)],
                    TimeSpan.FromSeconds(1),
                    32),
                CancellationToken.None));

        Assert.AreEqual("transport_output_oversized", exception.Code);
    }
}
