using Balls.Platform;
using Balls.Platform.Windows;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class WindowsCircleFilesOperationTests
{
    private static readonly WindowsCircleFilesHelperPlan Plan = new(
        new CircleFilesHostPlan(
            1,
            new string('a', 64),
            CircleFilesReadinessProviders.WindowsSmb311,
            @"C:\BallsShares\Company",
            "balls-019d2a6b1b66",
            "Balls-SMB-019d2a6b1b667d389c358d64ca8f8903",
            new string('b', 64),
            false,
            []),
        new CircleFilesHostRequest(
            "019d2a6b-1b66-7d38-9c35-8d64ca8f8901",
            "019d2a6b-1b66-7d38-9c35-8d64ca8f8902",
            "019d2a6b-1b66-7d38-9c35-8d64ca8f8903",
            "019d2a6b-1b66-7d38-9c35-8d64ca8f8904",
            "Company files",
            @"C:\BallsShares\Company",
            new string('c', 64)),
        "S-1-5-21-1000");

    [TestMethod]
    public async Task Clean_apply_creates_every_exact_resource_in_order()
    {
        var operations = new StubOperations();

        var status = await new WindowsCircleFilesOperation(operations)
            .ExecuteAsync(Plan, CancellationToken.None);

        Assert.AreEqual(CircleFilesHostApplyStatus.Applied, status);
        CollectionAssert.AreEqual(
            Enum.GetValues<WindowsCircleFilesOperationStep>(),
            operations.Applied.ToArray());
    }

    [TestMethod]
    public async Task Exact_retry_is_idempotent_and_does_not_mutate_again()
    {
        var operations = new StubOperations();
        foreach (var step in Enum.GetValues<WindowsCircleFilesOperationStep>())
        {
            operations.States[step] = WindowsCircleFilesOwnedState.Owned;
        }

        var status = await new WindowsCircleFilesOperation(operations)
            .ExecuteAsync(Plan, CancellationToken.None);

        Assert.AreEqual(CircleFilesHostApplyStatus.AlreadyApplied, status);
        Assert.AreEqual(0, operations.Applied.Count);
        Assert.AreEqual(0, operations.RolledBack.Count);
    }

    [TestMethod]
    public async Task Injected_failure_rolls_back_only_the_owned_applied_prefix_in_reverse()
    {
        var operations = new StubOperations
        {
            FailOn = WindowsCircleFilesOperationStep.PrivateFirewallRule,
        };

        var exception = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => new WindowsCircleFilesOperation(operations)
                .ExecuteAsync(Plan, CancellationToken.None).AsTask());

        Assert.AreEqual("hosting_apply_failed", exception.Code);
        CollectionAssert.AreEqual(
            new[]
            {
                WindowsCircleFilesOperationStep.EncryptedShare,
                WindowsCircleFilesOperationStep.OwnershipMarker,
                WindowsCircleFilesOperationStep.FolderAcl,
            },
            operations.RolledBack.ToArray());
        Assert.IsTrue(operations.States.Values.All(state => state == WindowsCircleFilesOwnedState.Missing));
    }

    [TestMethod]
    public async Task Recoverable_partial_state_is_rolled_back_then_reapplied()
    {
        var operations = new StubOperations();
        operations.States[WindowsCircleFilesOperationStep.FolderAcl] = WindowsCircleFilesOwnedState.Owned;
        operations.States[WindowsCircleFilesOperationStep.OwnershipMarker] = WindowsCircleFilesOwnedState.Owned;

        var status = await new WindowsCircleFilesOperation(operations)
            .ExecuteAsync(Plan, CancellationToken.None);

        Assert.AreEqual(CircleFilesHostApplyStatus.Applied, status);
        CollectionAssert.AreEqual(
            new[]
            {
                WindowsCircleFilesOperationStep.OwnershipMarker,
                WindowsCircleFilesOperationStep.FolderAcl,
            },
            operations.RolledBack.Take(2).ToArray());
    }

    [TestMethod]
    public async Task Preexisting_or_substituted_resource_collision_causes_no_mutation()
    {
        var operations = new StubOperations();
        operations.States[WindowsCircleFilesOperationStep.EncryptedShare] =
            WindowsCircleFilesOwnedState.Collision;

        var exception = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => new WindowsCircleFilesOperation(operations)
                .ExecuteAsync(Plan, CancellationToken.None).AsTask());

        Assert.AreEqual("hosting_resource_collision", exception.Code);
        Assert.AreEqual(0, operations.Applied.Count);
        Assert.AreEqual(0, operations.RolledBack.Count);
    }

    private sealed class StubOperations : IWindowsCircleFilesOperations
    {
        public Dictionary<WindowsCircleFilesOperationStep, WindowsCircleFilesOwnedState> States { get; } =
            Enum.GetValues<WindowsCircleFilesOperationStep>()
                .ToDictionary(step => step, _ => WindowsCircleFilesOwnedState.Missing);

        public WindowsCircleFilesOperationStep? FailOn { get; init; }

        public List<WindowsCircleFilesOperationStep> Applied { get; } = [];

        public List<WindowsCircleFilesOperationStep> RolledBack { get; } = [];

        public ValueTask<WindowsCircleFilesOwnedState> InspectAsync(
            WindowsCircleFilesHelperPlan plan,
            WindowsCircleFilesOperationStep step,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(States[step]);
        }

        public ValueTask ApplyAsync(
            WindowsCircleFilesHelperPlan plan,
            WindowsCircleFilesOperationStep step,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (step == FailOn)
            {
                throw new InvalidOperationException("injected");
            }

            Applied.Add(step);
            States[step] = WindowsCircleFilesOwnedState.Owned;
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(
            WindowsCircleFilesHelperPlan plan,
            WindowsCircleFilesOperationStep step,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RolledBack.Add(step);
            States[step] = WindowsCircleFilesOwnedState.Missing;
            return ValueTask.CompletedTask;
        }
    }
}
