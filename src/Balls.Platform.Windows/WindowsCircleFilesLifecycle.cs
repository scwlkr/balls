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

internal sealed record WindowsCircleFilesGrantCleanupHelperPlan(
    CircleFilesGrantCleanupPlan PublicPlan,
    CircleFilesGrantCleanupRequest Request,
    WindowsCircleFilesGrantHelperPlan GrantPlan,
    string OwnerSid,
    byte[] Secret,
    bool TerminateOpenSessions);

internal sealed record WindowsCircleFilesHostRemovalHelperPlan(
    CircleFilesHostRemovalPlan PublicPlan,
    CircleFilesHostRequest Request,
    WindowsCircleFilesHelperPlan HostPlan,
    string OwnerSid,
    bool TerminateOpenSessions);

internal interface IWindowsCircleFilesLifecycleHelperClient
{
    ValueTask<CircleFilesCleanupExecution> RemoveGrantAsync(
        WindowsCircleFilesGrantCleanupHelperPlan plan,
        CancellationToken cancellationToken);

    ValueTask<CircleFilesCleanupExecution> RemoveHostAsync(
        WindowsCircleFilesHostRemovalHelperPlan plan,
        CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsCircleFilesLifecycleManager : ICircleFilesLifecycleManager
{
    private readonly WindowsCircleFilesHostProvisioner hosting;
    private readonly WindowsCircleFilesGrantCredentialProvisioner grants;
    private readonly IWindowsCircleFilesLifecycleHelperClient helper;

    public WindowsCircleFilesLifecycleManager()
        : this(
            new WindowsCircleFilesHostProvisioner(),
            new WindowsCircleFilesGrantCredentialProvisioner(),
            new WindowsElevatedCircleFilesLifecycleHelperClient())
    {
    }

    internal WindowsCircleFilesLifecycleManager(
        WindowsCircleFilesHostProvisioner hosting,
        WindowsCircleFilesGrantCredentialProvisioner grants,
        IWindowsCircleFilesLifecycleHelperClient helper)
    {
        this.hosting = hosting;
        this.grants = grants;
        this.helper = helper;
    }

    public async ValueTask<CircleFilesGrantCleanupPlan> PreviewGrantCleanupAsync(
        CircleFilesGrantCleanupRequest request,
        CancellationToken cancellationToken) =>
        (await PrepareGrantAsync(request, [], false, cancellationToken).ConfigureAwait(false))
        .PublicPlan;

    public async ValueTask<CircleFilesGrantCleanupResult> RemoveGrantAsync(
        CircleFilesGrantCleanupRequest request,
        string expectedPlanId,
        ReadOnlyMemory<byte> secret,
        bool terminateOpenSessions,
        CancellationToken cancellationToken)
    {
        ValidatePlanId(expectedPlanId, "grant_cleanup_plan_changed");
        if (secret.Length is < 24 or > 128)
        {
            throw new CircleFilesHostingException(
                "grant_secret_invalid",
                "The Windows provider credential material is invalid.");
        }

        var prepared = await PrepareGrantAsync(
            request,
            secret.ToArray(),
            terminateOpenSessions,
            cancellationToken).ConfigureAwait(false);
        try
        {
            EnsurePlanId(prepared.PublicPlan.PlanId, expectedPlanId, "grant_cleanup_plan_changed");
            var result = await helper.RemoveGrantAsync(prepared, cancellationToken)
                .ConfigureAwait(false);
            return new CircleFilesGrantCleanupResult(
                result.Status,
                result.OpenSessionCount,
                prepared.PublicPlan);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prepared.Secret);
        }
    }

    public async ValueTask<CircleFilesHostRemovalPlan> PreviewHostRemovalAsync(
        CircleFilesHostRequest request,
        CancellationToken cancellationToken) =>
        (await PrepareHostAsync(request, false, cancellationToken).ConfigureAwait(false)).PublicPlan;

    public async ValueTask<CircleFilesHostRemovalResult> RemoveHostAsync(
        CircleFilesHostRequest request,
        string expectedPlanId,
        bool terminateOpenSessions,
        CancellationToken cancellationToken)
    {
        ValidatePlanId(expectedPlanId, "host_removal_plan_changed");
        var prepared = await PrepareHostAsync(request, terminateOpenSessions, cancellationToken)
            .ConfigureAwait(false);
        EnsurePlanId(prepared.PublicPlan.PlanId, expectedPlanId, "host_removal_plan_changed");
        var result = await helper.RemoveHostAsync(prepared, cancellationToken).ConfigureAwait(false);
        return new CircleFilesHostRemovalResult(
            result.Status,
            result.OpenSessionCount,
            prepared.PublicPlan);
    }

    internal async ValueTask<WindowsCircleFilesGrantCleanupHelperPlan> PrepareGrantForHelperAsync(
        CircleFilesGrantCleanupRequest request,
        byte[] secret,
        bool terminateOpenSessions,
        CancellationToken cancellationToken) =>
        await PrepareGrantAsync(request, secret, terminateOpenSessions, cancellationToken)
            .ConfigureAwait(false);

    internal async ValueTask<WindowsCircleFilesHostRemovalHelperPlan> PrepareHostForHelperAsync(
        CircleFilesHostRequest request,
        bool terminateOpenSessions,
        CancellationToken cancellationToken) =>
        await PrepareHostAsync(request, terminateOpenSessions, cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask<WindowsCircleFilesGrantCleanupHelperPlan> PrepareGrantAsync(
        CircleFilesGrantCleanupRequest request,
        byte[] secret,
        bool terminateOpenSessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        WindowsCircleFilesGrantAuthorizationVerifier.ValidateCleanup(request);
        var grantPlan = await grants.PrepareForRemovalAsync(
            request.Grant,
            secret,
            cancellationToken).ConfigureAwait(false);
        var planId = WindowsCircleFilesHostProvisioner.HashCanonical(
            "balls-windows-smb-grant-cleanup-plan-v1",
            grantPlan.PublicPlan.PlanId,
            request.Revocation.RequestId,
            request.Revocation.AuthorizationDigest);
        var plan = new CircleFilesGrantCleanupPlan(
            CircleFilesLifecycleContract.Version,
            planId,
            grantPlan.PublicPlan.Provider,
            grantPlan.PublicPlan.FolderPath,
            grantPlan.PublicPlan.ShareName,
            grantPlan.PublicPlan.AccountName,
            grantPlan.PublicPlan.OwnershipId,
            grantPlan.PublicPlan.Generation,
            [
                "Detect exact grant-owned SMB sessions and stop before mutation when any are open.",
                "After separate confirmation, terminate only sessions for the exact grant account.",
                "Remove the exact share and folder access, grant marker, deny rights, and local account.",
                "Preserve the contributed folder, user files, other grants, and foreign resources.",
            ]);
        return new WindowsCircleFilesGrantCleanupHelperPlan(
            plan,
            request,
            grantPlan,
            grantPlan.OwnerSid,
            secret,
            terminateOpenSessions);
    }

    private async ValueTask<WindowsCircleFilesHostRemovalHelperPlan> PrepareHostAsync(
        CircleFilesHostRequest request,
        bool terminateOpenSessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        WindowsCircleFilesHostAuthorizationVerifier.Validate(request);
        var hostPlan = await hosting.PrepareForRemovalAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var planId = WindowsCircleFilesHostProvisioner.HashCanonical(
            "balls-windows-smb-host-removal-plan-v1",
            hostPlan.PublicPlan.PlanId,
            request.AuthorizationDigest);
        var plan = new CircleFilesHostRemovalPlan(
            CircleFilesLifecycleContract.Version,
            planId,
            hostPlan.PublicPlan.Provider,
            hostPlan.PublicPlan.FolderPath,
            hostPlan.PublicPlan.ShareName,
            hostPlan.PublicPlan.FirewallRuleName,
            hostPlan.PublicPlan.OwnershipId,
            [
                "Refuse removal while any exact grant-owned permission or marker remains.",
                "Detect bounded SMB sessions whose open files are only inside this contribution.",
                "After separate confirmation, terminate only those exact sessions.",
                "Remove the exact firewall rule, encrypted share, ownership marker, and journal.",
                "Preserve the contributed folder and every user file.",
            ]);
        return new WindowsCircleFilesHostRemovalHelperPlan(
            plan,
            request,
            hostPlan,
            hostPlan.OwnerSid,
            terminateOpenSessions);
    }

    private static void ValidatePlanId(string? value, string code)
    {
        if (value is null
            || value.Length != 64
            || value.Any(character => !Uri.IsHexDigit(character) || char.IsUpper(character)))
        {
            throw new CircleFilesHostingException(
                code,
                "The Circle Files cleanup plan changed; preview it again before approval.");
        }
    }

    private static void EnsurePlanId(string actual, string expected, string code)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actual),
                Encoding.ASCII.GetBytes(expected)))
        {
            throw new CircleFilesHostingException(
                code,
                "The Circle Files cleanup plan changed; preview it again before approval.");
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsElevatedCircleFilesLifecycleHelperClient :
    IWindowsCircleFilesLifecycleHelperClient
{
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(2);
    private const int MaximumMessageBytes = 64 * 1024;

    public ValueTask<CircleFilesCleanupExecution> RemoveGrantAsync(
        WindowsCircleFilesGrantCleanupHelperPlan plan,
        CancellationToken cancellationToken) =>
        InvokeAsync("grant-remove", plan.OwnerSid, plan, cancellationToken);

    public ValueTask<CircleFilesCleanupExecution> RemoveHostAsync(
        WindowsCircleFilesHostRemovalHelperPlan plan,
        CancellationToken cancellationToken) =>
        InvokeAsync("host-remove", plan.OwnerSid, plan, cancellationToken);

    private static async ValueTask<CircleFilesCleanupExecution> InvokeAsync(
        string operation,
        string ownerSidValue,
        object plan,
        CancellationToken cancellationToken)
    {
        var helperPath = Path.Combine(AppContext.BaseDirectory, "balls-windows-helper.exe");
        if (!File.Exists(helperPath))
        {
            throw new CircleFilesHostingException(
                "hosting_helper_unavailable",
                "The Windows helper is unavailable.");
        }

        var pipeName = $"balls-host-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))}";
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var ownerSid = new SecurityIdentifier(ownerSidValue);
        pipeSecurity.SetOwner(ownerSid);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            ownerSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        await using var pipe = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            pipeSecurity);
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
                throw new CircleFilesHostingException(
                    "hosting_helper_unavailable",
                    "The Windows helper could not start.");
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new CircleFilesHostingException(
                "hosting_consent_cancelled",
                "The Windows administrator approval was cancelled.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ApprovalTimeout);
        try
        {
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (!WindowsNamedPipeProcessIdentity.TryGetClientProcessId(pipe, out var clientPid)
                || clientPid != helper.Id)
            {
                throw new CircleFilesHostingException(
                    "hosting_helper_authentication_failed",
                    "The elevated helper connection could not be authenticated.");
            }

            var envelope = operation == "grant-remove"
                ? new WindowsCircleFilesHelperEnvelope(
                    operation,
                    null,
                    null,
                    (WindowsCircleFilesGrantCleanupHelperPlan)plan,
                    null)
                : new WindowsCircleFilesHelperEnvelope(
                    operation,
                    null,
                    null,
                    null,
                    (WindowsCircleFilesHostRemovalHelperPlan)plan);
            await WindowsCircleFilesHelperProtocol.WriteAsync(
                pipe,
                envelope,
                MaximumMessageBytes,
                timeout.Token).ConfigureAwait(false);
            var response = await WindowsCircleFilesHelperProtocol.ReadAsync<WindowsCircleFilesHelperResponse>(
                pipe,
                MaximumMessageBytes,
                timeout.Token).ConfigureAwait(false);
            if (response.ErrorCode is not null)
            {
                throw new CircleFilesHostingException(response.ErrorCode, response.Message);
            }

            var status = response.Status switch
            {
                "removed" => CircleFilesCleanupStatus.Removed,
                "already-removed" => CircleFilesCleanupStatus.AlreadyRemoved,
                "busy" => CircleFilesCleanupStatus.Busy,
                "partial" => CircleFilesCleanupStatus.Partial,
                _ => throw new CircleFilesHostingException(
                    "hosting_helper_invalid_response",
                    "The Windows helper returned an invalid response."),
            };
            return new CircleFilesCleanupExecution(status, response.OpenSessionCount);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CircleFilesHostingException(
                "hosting_consent_timeout",
                "Windows administrator approval timed out.");
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new CircleFilesHostingException(
                "hosting_helper_invalid_response",
                "The Windows helper returned an invalid response.");
        }
    }
}
