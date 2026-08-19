using System.Text.RegularExpressions;

namespace Balls.Verify.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed partial class RepositoryWorkflowTests
{
    private static readonly string Workflow = File.ReadAllText(
        Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ci.yml"));

    [TestMethod]
    public void Required_lanes_use_fixed_images_and_stable_names()
    {
        Assert.IsFalse(Workflow.Contains("-latest", StringComparison.Ordinal));
        StringAssert.Contains(Workflow, "windows-fast:");
        StringAssert.Contains(Workflow, "name: Windows fast");
        StringAssert.Contains(Workflow, "runs-on: windows-2025");
        StringAssert.Contains(Workflow, "ubuntu-fast:");
        StringAssert.Contains(Workflow, "name: Ubuntu fast");
        StringAssert.Contains(Workflow, "runs-on: ubuntu-24.04");
    }

    [TestMethod]
    public void Aggregate_is_fail_closed_over_both_platform_lanes()
    {
        StringAssert.Contains(Workflow, "required:");
        StringAssert.Contains(Workflow, "name: Required");
        StringAssert.Contains(Workflow, "needs: [windows-fast, ubuntu-fast]");
        StringAssert.Contains(Workflow, "if: ${{ always() }}");
        StringAssert.Contains(Workflow, "WINDOWS_RESULT: ${{ needs.windows-fast.result }}");
        StringAssert.Contains(Workflow, "UBUNTU_RESULT: ${{ needs.ubuntu-fast.result }}");
    }

    [TestMethod]
    public void Workflow_preserves_supply_chain_and_concurrency_guards()
    {
        StringAssert.Contains(Workflow, "permissions:");
        StringAssert.Contains(Workflow, "contents: read");
        StringAssert.Contains(Workflow, "cancel-in-progress: true");
        StringAssert.Contains(Workflow, "cache: true");

        var usesLines = Workflow.Split('\n')
            .Where(line => line.TrimStart().StartsWith("uses:", StringComparison.Ordinal))
            .ToArray();
        Assert.IsNotEmpty(usesLines);
        Assert.IsTrue(usesLines.All(line => ShaPinnedAction().IsMatch(line)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Balls.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Balls.slnx from the test directory.");
    }

    [GeneratedRegex(@"^\s*uses:\s+[^@\s]+@[0-9a-f]{40}(?:\s+#.*)?$", RegexOptions.Multiline)]
    private static partial Regex ShaPinnedAction();
}
