using Balls.Verify;

namespace Balls.Verify.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class VerificationTests
{
    [TestMethod]
    public void Focused_request_requires_a_project_and_filter()
    {
        var request = VerificationRequestParser.Parse(
            ["focused", "--project", "tests/Balls.Core.Tests", "--filter", "TestCategory=Unit"]);

        Assert.AreEqual(VerificationMode.Focused, request.Mode);
        Assert.AreEqual("tests/Balls.Core.Tests", request.Project);
        Assert.AreEqual("TestCategory=Unit", request.Filter);
        Assert.ThrowsExactly<UsageException>(() =>
            VerificationRequestParser.Parse(["focused", "--project", "tests/Balls.Core.Tests"]));
    }

    [TestMethod]
    public void Focused_web_request_accepts_only_repository_standard_scripts()
    {
        var request = VerificationRequestParser.Parse(
            ["focused", "--web", "generate:check"]);

        Assert.AreEqual(VerificationMode.Focused, request.Mode);
        Assert.AreEqual("generate:check", request.WebScript);
        Assert.ThrowsExactly<UsageException>(() =>
            VerificationRequestParser.Parse(["focused", "--web", "arbitrary"]));
    }

    [TestMethod]
    public void Focused_web_plan_installs_the_lockfile_and_runs_one_selected_script()
    {
        var plan = VerificationPlanner.Create(
            new VerificationRequest(VerificationMode.Focused, WebScript: "test"),
            "C:/repo",
            "C:/results");

        Assert.AreEqual(2, plan.Steps.Count);
        CollectionAssert.AreEqual(
            new[] { "install", "--frozen-lockfile" },
            plan.Steps[0].Command.Arguments.TakeLast(2).ToArray());
        CollectionAssert.AreEqual(
            new[] { "web:test" },
            plan.Steps[1].Command.Arguments.TakeLast(1).ToArray());
    }

    [TestMethod]
    public void Fast_plan_builds_once_and_selects_only_portable_safe_categories()
    {
        var plan = VerificationPlanner.Create(
            new VerificationRequest(VerificationMode.Fast),
            "C:/repo",
            "C:/results");

        Assert.AreEqual(1, CountDotnetVerb(plan, "restore"));
        Assert.AreEqual(1, CountDotnetVerb(plan, "build"));
        Assert.AreEqual(19, plan.Steps.Count);
        Assert.AreEqual(TestCountRule.RequireZero, plan.Steps[11].TestCountRule);
        CollectionAssert.Contains(
            plan.Steps[12].Command.Arguments.ToArray(),
            "(TestCategory=Unit|TestCategory=Contract|TestCategory=ProcessIntegration)");
        CollectionAssert.DoesNotContain(
            plan.Steps[12].Command.Arguments.ToArray(),
            "TestCategory=OSIntegration");
        CollectionAssert.AreEqual(
            new[]
            {
                "web:generate:check",
                "web:format:check",
                "downloads:format:check",
                "web:lint",
                "downloads:lint",
                "web:typecheck",
                "downloads:typecheck",
                "web:test",
                "web:build",
                "web:e2e",
                "downloads:build",
                "downloads:test",
                "downloads:e2e",
            },
            PnpmScripts(plan));
    }

    [TestMethod]
    public void Full_plan_preserves_the_release_gate_and_checks_category_coverage()
    {
        var plan = VerificationPlanner.Create(
            new VerificationRequest(VerificationMode.Full),
            "C:/repo",
            "C:/results");

        Assert.AreEqual(19, plan.Steps.Count);
        CollectionAssert.AreEqual(
            new[] { "restore", "format", "build", "test", "test" },
            plan.Steps
                .Where(step => step.Command.FileName == "dotnet")
                .Select(step => step.Command.Arguments[0])
                .ToArray());
        Assert.AreEqual(TestCountRule.RequireZero, plan.Steps[11].TestCountRule);
        CollectionAssert.DoesNotContain(plan.Steps[12].Command.Arguments.ToArray(), "--filter");
        CollectionAssert.Contains(plan.Steps[12].Command.Arguments.ToArray(), "--no-build");
        CollectionAssert.Contains(plan.Steps[12].Command.Arguments.ToArray(), "--no-restore");
    }

    [TestMethod]
    public void Trx_summary_adds_results_from_every_test_assembly()
    {
        using var directory = new TemporaryDirectory();
        WriteTrx(directory.Path, "first.trx", 2);
        WriteTrx(directory.Path, "second.trx", 3);

        Assert.AreEqual(5, TrxSummary.ReadTotal(directory.Path));
    }

    [TestMethod]
    public async Task Focused_mode_fails_when_the_filter_selects_zero_tests()
    {
        using var directory = new TemporaryDirectory();
        var plan = new VerificationPlan(
            [new VerificationStep(
                new VerificationCommand("dotnet", ["test"]),
                TestCountRule.RequireAtLeastOne,
                directory.Path)]);
        var engine = new VerificationEngine(
            new TrxWritingRunner(directory.Path, total: 0, exitCode: 0));

        var exitCode = await engine.ExecuteAsync(plan);

        Assert.AreEqual(VerificationExitCodes.NoTestsSelected, exitCode);
    }

    [TestMethod]
    public async Task Category_audit_fails_when_any_test_is_unclassified()
    {
        using var directory = new TemporaryDirectory();
        var plan = new VerificationPlan(
            [new VerificationStep(
                new VerificationCommand("dotnet", ["test"]),
                TestCountRule.RequireZero,
                directory.Path)]);
        var engine = new VerificationEngine(
            new TrxWritingRunner(directory.Path, total: 1, exitCode: 0));

        var exitCode = await engine.ExecuteAsync(plan);

        Assert.AreEqual(VerificationExitCodes.UnclassifiedTests, exitCode);
    }

    [TestMethod]
    public async Task Command_failure_is_returned_without_running_later_steps()
    {
        var runner = new FailingRunner();
        var plan = new VerificationPlan(
            [
                new VerificationStep(new VerificationCommand("dotnet", ["restore"])),
                new VerificationStep(new VerificationCommand("dotnet", ["build"])),
            ]);
        var engine = new VerificationEngine(runner);

        var exitCode = await engine.ExecuteAsync(plan);

        Assert.AreEqual(17, exitCode);
        Assert.AreEqual(1, runner.CallCount);
    }

    private static int CountDotnetVerb(VerificationPlan plan, string verb) =>
        plan.Steps.Count(step =>
            step.Command.FileName == "dotnet" && step.Command.Arguments[0] == verb);

    private static string[] PnpmScripts(VerificationPlan plan) =>
        plan.Steps
            .Where(step =>
                step.Command.FileName == "pnpm"
                || step.Command.Arguments.Contains("pnpm", StringComparer.Ordinal))
            .Select(step => step.Command.Arguments[^1])
            .Where(argument =>
                argument.StartsWith("web:", StringComparison.Ordinal)
                || argument.StartsWith("downloads:", StringComparison.Ordinal))
            .ToArray();

    private static void WriteTrx(string directory, string fileName, int total)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, fileName),
            $"<TestRun><ResultSummary><Counters total=\"{total}\" /></ResultSummary></TestRun>");
    }

    private sealed class TrxWritingRunner(string directory, int total, int exitCode) : ICommandRunner
    {
        public Task<int> RunAsync(
            VerificationCommand command,
            CancellationToken cancellationToken)
        {
            WriteTrx(directory, "results.trx", total);
            return Task.FromResult(exitCode);
        }
    }

    private sealed class FailingRunner : ICommandRunner
    {
        public int CallCount { get; private set; }

        public Task<int> RunAsync(
            VerificationCommand command,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(17);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"balls-verify-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
