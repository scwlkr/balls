using Balls.Platform;

namespace Balls.Platform.Windows;

internal enum WindowsCircleFilesOperationStep
{
    FolderAcl,
    OwnershipMarker,
    EncryptedShare,
    PrivateFirewallRule,
}

internal enum WindowsCircleFilesOwnedState
{
    Missing,
    Blocked,
    Recoverable,
    Owned,
    Collision,
}

internal interface IWindowsCircleFilesOperations
{
    ValueTask<WindowsCircleFilesOwnedState> InspectAsync(
        WindowsCircleFilesHelperPlan plan,
        WindowsCircleFilesOperationStep step,
        CancellationToken cancellationToken);

    ValueTask ApplyAsync(
        WindowsCircleFilesHelperPlan plan,
        WindowsCircleFilesOperationStep step,
        CancellationToken cancellationToken);

    ValueTask RollbackAsync(
        WindowsCircleFilesHelperPlan plan,
        WindowsCircleFilesOperationStep step,
        CancellationToken cancellationToken);
}

internal sealed class WindowsCircleFilesOperation(IWindowsCircleFilesOperations operations)
{
    private static readonly WindowsCircleFilesOperationStep[] Steps =
    [
        WindowsCircleFilesOperationStep.FolderAcl,
        WindowsCircleFilesOperationStep.OwnershipMarker,
        WindowsCircleFilesOperationStep.EncryptedShare,
        WindowsCircleFilesOperationStep.PrivateFirewallRule,
    ];

    internal async ValueTask<CircleFilesHostApplyStatus> ExecuteAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken)
    {
        var states = await InspectAllAsync(plan, cancellationToken).ConfigureAwait(false);
        if (states.Any(pair => pair.Value == WindowsCircleFilesOwnedState.Collision))
        {
            throw Collision();
        }

        if (states.All(pair => pair.Value == WindowsCircleFilesOwnedState.Owned))
        {
            return CircleFilesHostApplyStatus.AlreadyApplied;
        }

        if (states.Any(pair => pair.Value == WindowsCircleFilesOwnedState.Owned))
        {
            await RollbackOwnedAsync(plan, states, cancellationToken).ConfigureAwait(false);
            states = await InspectAllAsync(plan, cancellationToken).ConfigureAwait(false);
            if (states.Any(pair => pair.Value != WindowsCircleFilesOwnedState.Missing))
            {
                throw new CircleFilesHostingException(
                    "hosting_recovery_incomplete",
                    "A prior partial hosting operation could not be recovered safely.");
            }
        }

        try
        {
            foreach (var step in Steps)
            {
                await operations.ApplyAsync(plan, step, cancellationToken).ConfigureAwait(false);
                var state = await operations.InspectAsync(plan, step, cancellationToken)
                    .ConfigureAwait(false);
                if (state != WindowsCircleFilesOwnedState.Owned)
                {
                    throw new InvalidOperationException("The hosting step did not create its exact owned state.");
                }

            }

            return CircleFilesHostApplyStatus.Applied;
        }
        catch (OperationCanceledException)
        {
            await RollbackCurrentOwnedAsync(plan, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            try
            {
                await RollbackCurrentOwnedAsync(plan, CancellationToken.None).ConfigureAwait(false);
            }
            catch (CircleFilesHostingException)
            {
                throw;
            }

            if (exception is CircleFilesHostingException hostingException)
            {
                throw hostingException;
            }

            throw new CircleFilesHostingException(
                "hosting_apply_failed",
                "Windows could not complete the dedicated Circle Files hosting operation.");
        }
    }

    private async ValueTask<Dictionary<WindowsCircleFilesOperationStep, WindowsCircleFilesOwnedState>>
        InspectAllAsync(
            WindowsCircleFilesHelperPlan plan,
            CancellationToken cancellationToken)
    {
        var result = new Dictionary<WindowsCircleFilesOperationStep, WindowsCircleFilesOwnedState>();
        foreach (var step in Steps)
        {
            result.Add(
                step,
                await operations.InspectAsync(plan, step, cancellationToken).ConfigureAwait(false));
        }

        return result;
    }

    private async ValueTask RollbackCurrentOwnedAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken)
    {
        foreach (var step in Steps.Reverse())
        {
            var state = await operations.InspectAsync(plan, step, cancellationToken)
                .ConfigureAwait(false);
            if (state == WindowsCircleFilesOwnedState.Collision)
            {
                throw Collision();
            }

            if (state == WindowsCircleFilesOwnedState.Owned)
            {
                await operations.RollbackAsync(plan, step, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask RollbackOwnedAsync(
        WindowsCircleFilesHelperPlan plan,
        IReadOnlyDictionary<WindowsCircleFilesOperationStep, WindowsCircleFilesOwnedState> states,
        CancellationToken cancellationToken)
    {
        foreach (var step in Steps.Reverse())
        {
            if (states[step] == WindowsCircleFilesOwnedState.Owned)
            {
                await operations.RollbackAsync(plan, step, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static CircleFilesHostingException Collision() =>
        new(
            "hosting_resource_collision",
            "A required Windows resource exists but is not owned by this contribution operation.");
}
