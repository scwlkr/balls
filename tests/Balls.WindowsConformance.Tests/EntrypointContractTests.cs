namespace Balls.WindowsConformance.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class EntrypointContractTests
{
    [TestMethod]
    public void Linux_entrypoint_gives_managed_cleanup_time_after_cancellation()
    {
        var script = File.ReadAllText(RepositoryPath(
            "eng",
            "conformance",
            "Test-WindowsSmbReadiness.sh"));

        StringAssert.Contains(script, "--signal=INT");
        StringAssert.Contains(script, "--kill-after=75s");
    }

    [TestMethod]
    public void Managed_entrypoint_converts_interrupts_to_runner_cancellation()
    {
        var program = File.ReadAllText(RepositoryPath(
            "eng",
            "Balls.WindowsConformance",
            "Program.cs"));

        StringAssert.Contains(program, "Console.CancelKeyPress");
        StringAssert.Contains(program, "PosixSignal.SIGTERM");
        StringAssert.Contains(program, "cancellation.Token");
    }

    private static string RepositoryPath(params string[] parts)
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Balls.slnx")))
            {
                return Path.Combine([directory.FullName, .. parts]);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
