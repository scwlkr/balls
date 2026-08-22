using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Balls.Platform;

namespace Balls.Platform.Windows;

internal sealed record WindowsCircleFilesGrantHelperPlan(
    CircleFilesGrantCredentialPlan PublicPlan,
    CircleFilesGrantCredentialRequest Request,
    WindowsCircleFilesHelperPlan HostPlan,
    string OwnerSid,
    byte[] Secret);

internal interface IWindowsCircleFilesGrantHelperClient
{
    ValueTask<CircleFilesGrantCredentialApplyStatus> ApplyAsync(
        WindowsCircleFilesGrantHelperPlan plan,
        CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsCircleFilesGrantCredentialProvisioner :
    ICircleFilesGrantCredentialProvisioner
{
    private readonly WindowsCircleFilesHostProvisioner hosting;
    private readonly IWindowsCircleFilesGrantHelperClient helper;

    public WindowsCircleFilesGrantCredentialProvisioner()
        : this(
            new WindowsCircleFilesHostProvisioner(),
            new WindowsElevatedCircleFilesGrantHelperClient())
    {
    }

    internal WindowsCircleFilesGrantCredentialProvisioner(
        WindowsCircleFilesHostProvisioner hosting,
        IWindowsCircleFilesGrantHelperClient helper)
    {
        this.hosting = hosting;
        this.helper = helper;
    }

    public async ValueTask<CircleFilesGrantCredentialPlan> PreviewAsync(
        CircleFilesGrantCredentialRequest request,
        CancellationToken cancellationToken) =>
        (await PrepareAsync(request, secret: [], cancellationToken).ConfigureAwait(false)).PublicPlan;

    public async ValueTask<CircleFilesGrantCredentialApplyResult> ApplyAsync(
        CircleFilesGrantCredentialRequest request,
        string expectedPlanId,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken)
    {
        if (!IsLowerHex(expectedPlanId, 64))
        {
            throw PlanChanged();
        }

        if (secret.Length is < 24 or > 128)
        {
            throw new CircleFilesHostingException(
                "grant_secret_invalid",
                "The Windows provider credential material is invalid.");
        }

        var prepared = await PrepareAsync(request, secret.ToArray(), cancellationToken)
            .ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(prepared.PublicPlan.PlanId),
                Encoding.ASCII.GetBytes(expectedPlanId)))
        {
            CryptographicOperations.ZeroMemory(prepared.Secret);
            throw PlanChanged();
        }

        try
        {
            var status = await helper.ApplyAsync(prepared, cancellationToken).ConfigureAwait(false);
            return new CircleFilesGrantCredentialApplyResult(status, prepared.PublicPlan);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prepared.Secret);
        }
    }

    internal async ValueTask<WindowsCircleFilesGrantHelperPlan> PrepareForHelperAsync(
        CircleFilesGrantCredentialRequest request,
        byte[] secret,
        CancellationToken cancellationToken) =>
        await PrepareAsync(request, secret, cancellationToken).ConfigureAwait(false);

    private async ValueTask<WindowsCircleFilesGrantHelperPlan> PrepareAsync(
        CircleFilesGrantCredentialRequest request,
        byte[] secret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCanonicalId(request.GrantId);
        ValidateCanonicalId(request.MemberId);
        if (request.Generation <= 0
            || request.Access is not ("read-only" or "read-write")
            || !IsLowerHex(request.AuthorizationDigest, 64)
            || request.Authorization is null
            || !string.Equals(
                CircleFilesHostAuthorizationDigest.Compute(request.Authorization),
                request.AuthorizationDigest,
                StringComparison.Ordinal))
        {
            throw InvalidAuthorization();
        }

        var hostPlan = await hosting.PrepareForHelperAsync(request.Host, cancellationToken)
            .ConfigureAwait(false);
        var ownershipId = WindowsCircleFilesHostProvisioner.HashCanonical(
            "balls-windows-smb-grant-ownership-v1",
            request.Host.CircleId,
            request.Host.ContributionId,
            request.GrantId,
            request.MemberId,
            request.Access,
            request.Generation.ToString(CultureInfo.InvariantCulture),
            request.AuthorizationDigest,
            hostPlan.PublicPlan.OwnershipId);
        var accountName = "BallsG-" + ownershipId[..13];
        var planId = WindowsCircleFilesHostProvisioner.HashCanonical(
            "balls-windows-smb-grant-plan-v1",
            ownershipId,
            accountName,
            hostPlan.PublicPlan.PlanId,
            hostPlan.OwnerSid);
        var plan = new CircleFilesGrantCredentialPlan(
            CircleFilesGrantCredentialContract.Version,
            planId,
            CircleFilesReadinessProviders.WindowsSmb311,
            hostPlan.PublicPlan.FolderPath,
            hostPlan.PublicPlan.ShareName,
            accountName,
            ownershipId,
            request.Access,
            request.Generation,
            [
                "Create the exact grant-owned local account with a random password and deny local logons.",
                $"Grant whole-folder {(request.Access == "read-only" ? "read-only" : "read/write")} access to that account.",
                "Grant only the matching encrypted SMB share access and record exact ownership metadata.",
            ]);
        return new WindowsCircleFilesGrantHelperPlan(
            plan,
            request,
            hostPlan,
            hostPlan.OwnerSid,
            secret);
    }

    private static void ValidateCanonicalId(string value)
    {
        if (!Guid.TryParseExact(value, "D", out var parsed)
            || parsed == Guid.Empty
            || parsed.ToString("D") != value)
        {
            throw InvalidAuthorization();
        }
    }

    private static bool IsLowerHex(string? value, int length) =>
        value is not null
        && value.Length == length
        && value.All(character => Uri.IsHexDigit(character) && !char.IsUpper(character));

    private static CircleFilesHostingException InvalidAuthorization() => new(
        "grant_authorization_invalid",
        "The Member Access Grant authorization binding is invalid.");

    private static CircleFilesHostingException PlanChanged() => new(
        "grant_plan_changed",
        "The Windows Member credential plan changed; preview it again before approval.");
}

internal enum WindowsCircleFilesGrantOperationStep
{
    LocalAccount,
    GrantMarker,
    FolderAcl,
    ShareAccess,
}

internal interface IWindowsCircleFilesGrantOperations
{
    ValueTask<WindowsCircleFilesOwnedState> InspectAsync(
        WindowsCircleFilesGrantHelperPlan plan,
        WindowsCircleFilesGrantOperationStep step,
        CancellationToken cancellationToken);

    ValueTask ApplyAsync(
        WindowsCircleFilesGrantHelperPlan plan,
        WindowsCircleFilesGrantOperationStep step,
        CancellationToken cancellationToken);

    ValueTask RollbackAsync(
        WindowsCircleFilesGrantHelperPlan plan,
        WindowsCircleFilesGrantOperationStep step,
        CancellationToken cancellationToken);
}

internal sealed class WindowsCircleFilesGrantOperation(IWindowsCircleFilesGrantOperations operations)
{
    private static readonly WindowsCircleFilesGrantOperationStep[] Steps =
    [
        WindowsCircleFilesGrantOperationStep.LocalAccount,
        WindowsCircleFilesGrantOperationStep.GrantMarker,
        WindowsCircleFilesGrantOperationStep.FolderAcl,
        WindowsCircleFilesGrantOperationStep.ShareAccess,
    ];

    internal async ValueTask<CircleFilesGrantCredentialApplyStatus> ExecuteAsync(
        WindowsCircleFilesGrantHelperPlan plan,
        CancellationToken cancellationToken)
    {
        var states = await InspectAllAsync(plan, cancellationToken).ConfigureAwait(false);
        if (states.Any(value => value.Value == WindowsCircleFilesOwnedState.Collision))
        {
            throw Collision();
        }
        if (states.Any(value => value.Value == WindowsCircleFilesOwnedState.Blocked))
        {
            await RollbackAsync(plan, states, cancellationToken).ConfigureAwait(false);
            throw Collision();
        }
        if (states.All(value => value.Value == WindowsCircleFilesOwnedState.Owned))
        {
            return CircleFilesGrantCredentialApplyStatus.AlreadyApplied;
        }
        if (states.Any(value => value.Value is WindowsCircleFilesOwnedState.Owned
                or WindowsCircleFilesOwnedState.Recoverable))
        {
            await RollbackAsync(plan, states, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            foreach (var step in Steps)
            {
#if DEBUG
                if (Environment.GetEnvironmentVariable("BALLS_TEST_WINDOWS_GRANT_FAILURE_STEP")
                    == step.ToString())
                {
                    throw new InvalidOperationException("A bounded debug-only grant failure was injected.");
                }
#endif
                await operations.ApplyAsync(plan, step, cancellationToken).ConfigureAwait(false);
                if (await operations.InspectAsync(plan, step, cancellationToken).ConfigureAwait(false)
                    != WindowsCircleFilesOwnedState.Owned)
                {
                    throw new InvalidOperationException("The grant step did not create exact owned state.");
                }
            }
            return CircleFilesGrantCredentialApplyStatus.Applied;
        }
        catch (OperationCanceledException)
        {
            await RollbackCurrentAsync(plan, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await RollbackCurrentAsync(plan, CancellationToken.None).ConfigureAwait(false);
            if (exception is CircleFilesHostingException known)
            {
                throw known;
            }
            throw new CircleFilesHostingException(
                "grant_apply_failed",
                "Windows could not complete the Member credential operation.");
        }
    }

    private async ValueTask<Dictionary<WindowsCircleFilesGrantOperationStep, WindowsCircleFilesOwnedState>>
        InspectAllAsync(WindowsCircleFilesGrantHelperPlan plan, CancellationToken cancellationToken)
    {
        var states = new Dictionary<WindowsCircleFilesGrantOperationStep, WindowsCircleFilesOwnedState>();
        foreach (var step in Steps)
        {
            states[step] = await operations.InspectAsync(plan, step, cancellationToken)
                .ConfigureAwait(false);
        }
        return states;
    }

    private async ValueTask RollbackCurrentAsync(
        WindowsCircleFilesGrantHelperPlan plan,
        CancellationToken cancellationToken)
    {
        var states = await InspectAllAsync(plan, cancellationToken).ConfigureAwait(false);
        if (states.Any(value => value.Value == WindowsCircleFilesOwnedState.Collision))
        {
            throw Collision();
        }
        await RollbackAsync(plan, states, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RollbackAsync(
        WindowsCircleFilesGrantHelperPlan plan,
        IReadOnlyDictionary<WindowsCircleFilesGrantOperationStep, WindowsCircleFilesOwnedState> states,
        CancellationToken cancellationToken)
    {
        foreach (var step in Steps.Reverse())
        {
            if (states[step] is WindowsCircleFilesOwnedState.Owned
                or WindowsCircleFilesOwnedState.Recoverable)
            {
                await operations.RollbackAsync(plan, step, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static CircleFilesHostingException Collision() => new(
        "grant_resource_collision",
        "A Windows account or permission exists but is not exactly owned by this Access Grant.");
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsElevatedCircleFilesGrantHelperClient :
    IWindowsCircleFilesGrantHelperClient
{
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(2);
    private const int MaximumMessageBytes = 64 * 1024;

    public async ValueTask<CircleFilesGrantCredentialApplyStatus> ApplyAsync(
        WindowsCircleFilesGrantHelperPlan plan,
        CancellationToken cancellationToken)
    {
        var helperPath = Path.Combine(AppContext.BaseDirectory, "balls-windows-helper.exe");
        if (!File.Exists(helperPath))
        {
            throw new CircleFilesHostingException("hosting_helper_unavailable", "The Windows helper is unavailable.");
        }

        var pipeName = $"balls-host-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))}";
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var ownerSid = new SecurityIdentifier(plan.OwnerSid);
        pipeSecurity.SetOwner(ownerSid);
        pipeSecurity.AddAccessRule(new PipeAccessRule(ownerSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        await using var pipe = NamedPipeServerStreamAcl.Create(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            0, 0, pipeSecurity);
        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add("--pipe-name");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--server-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        using var helper = new Process { StartInfo = startInfo };
        try
        {
            if (!helper.Start())
            {
                throw new CircleFilesHostingException("hosting_helper_unavailable", "The Windows helper could not start.");
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new CircleFilesHostingException("hosting_consent_cancelled", "Windows administrator approval was cancelled.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ApprovalTimeout);
        try
        {
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (!WindowsNamedPipeProcessIdentity.TryGetClientProcessId(pipe, out var clientPid)
                || clientPid != helper.Id)
            {
                throw new CircleFilesHostingException("hosting_helper_authentication_failed", "The elevated helper connection could not be authenticated.");
            }
            await WindowsCircleFilesHelperProtocol.WriteAsync(
                pipe,
                new WindowsCircleFilesHelperEnvelope("grant", null, plan),
                MaximumMessageBytes,
                timeout.Token).ConfigureAwait(false);
            var response = await WindowsCircleFilesHelperProtocol.ReadAsync<WindowsCircleFilesHelperResponse>(
                pipe, MaximumMessageBytes, timeout.Token).ConfigureAwait(false);
            if (response.ErrorCode is not null)
            {
                throw new CircleFilesHostingException(response.ErrorCode, response.Message);
            }
            return response.Status switch
            {
                "applied" => CircleFilesGrantCredentialApplyStatus.Applied,
                "already-applied" => CircleFilesGrantCredentialApplyStatus.AlreadyApplied,
                _ => throw new CircleFilesHostingException("hosting_helper_invalid_response", "The Windows helper returned an invalid response."),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CircleFilesHostingException("hosting_consent_timeout", "Windows administrator approval timed out.");
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new CircleFilesHostingException("hosting_helper_invalid_response", "The Windows helper returned an invalid response.");
        }
    }
}
