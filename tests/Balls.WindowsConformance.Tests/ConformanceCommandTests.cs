using Balls.WindowsConformance;

namespace Balls.WindowsConformance.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class ConformanceCommandTests
{
    [TestMethod]
    public async Task Unknown_command_is_a_usage_error()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ConformanceCommand.RunAsync(
            ["shell", "--command", "whoami"],
            output,
            error);

        Assert.AreEqual(ConformanceCommand.UsageError, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.StartsWith(error.ToString(), "Usage: Balls.WindowsConformance <run|host-run>");
        Assert.IsFalse(error.ToString().Contains("whoami", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Unauthorized_target_returns_only_the_stable_refusal_code()
    {
        using var profile = TargetProfileFixture.Create(authorized: false);
        using var package = PackageFixture.Create("0123456789abcdef0123456789abcdef01234567");
        using var receiptDirectory = TemporaryDirectory.Create();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ConformanceCommand.RunAsync(
            [
                "run",
                "--target-profile",
                profile.Path,
                "--package",
                package.PackagePath,
                "--checksum",
                package.ChecksumPath,
                "--expected-commit",
                "0123456789abcdef0123456789abcdef01234567",
                "--receipt",
                Path.Combine(receiptDirectory.Path, "receipt.json"),
            ],
            output,
            error);

        Assert.AreEqual(ConformanceCommand.Refused, exitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(
            $"windows-conformance: target_not_authorized{Environment.NewLine}",
            error.ToString());
    }
}
