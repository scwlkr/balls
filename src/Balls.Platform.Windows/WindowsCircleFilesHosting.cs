using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Balls.Platform;

namespace Balls.Platform.Windows;

internal sealed record WindowsCircleFilesHelperPlan(
    CircleFilesHostPlan PublicPlan,
    CircleFilesHostRequest Request,
    string OwnerSid);

internal interface IWindowsCircleFilesHelperClient
{
    ValueTask<CircleFilesHostApplyStatus> ApplyAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken);
}

internal interface IWindowsCircleFilesPathEnvironment
{
    string CurrentUserSid { get; }

    string GetFullPath(string path);

    string GetPathRoot(string path);

    bool IsFixedLocalDrive(string root);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    bool HasReparsePointInExistingPath(string path);

    IReadOnlyList<string> EnumerateEntries(string path);

    string? ReadAllText(string path);

    IReadOnlyList<string> RefusedRoots { get; }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsCircleFilesHostProvisioner : ICircleFilesHostProvisioner
{
    private readonly ICircleFilesReadinessInspector readiness;
    private readonly IWindowsCircleFilesPathEnvironment environment;
    private readonly IWindowsCircleFilesHelperClient helper;

    public WindowsCircleFilesHostProvisioner()
        : this(
            new WindowsSmbReadinessInspector(),
            new WindowsCircleFilesPathEnvironment(),
            new WindowsElevatedCircleFilesHelperClient())
    {
    }

    internal WindowsCircleFilesHostProvisioner(
        ICircleFilesReadinessInspector readiness,
        IWindowsCircleFilesPathEnvironment environment,
        IWindowsCircleFilesHelperClient helper)
    {
        this.readiness = readiness;
        this.environment = environment;
        this.helper = helper;
    }

    public async ValueTask<CircleFilesHostPlan> PreviewAsync(
        CircleFilesHostRequest request,
        CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        return prepared.PublicPlan;
    }

    public async ValueTask<CircleFilesHostApplyResult> ApplyAsync(
        CircleFilesHostRequest request,
        string expectedPlanId,
        CancellationToken cancellationToken)
    {
        if (expectedPlanId is null
            || expectedPlanId.Length != 64
            || expectedPlanId.Any(character => !Uri.IsHexDigit(character) || char.IsUpper(character)))
        {
            throw new CircleFilesHostingException(
                "hosting_plan_changed",
                "The Circle Files hosting plan changed; preview it again before approval.");
        }
        var prepared = await PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(prepared.PublicPlan.PlanId),
                Encoding.ASCII.GetBytes(expectedPlanId)))
        {
            throw new CircleFilesHostingException(
                "hosting_plan_changed",
                "The Circle Files hosting plan changed; preview it again before approval.");
        }

        var status = await helper.ApplyAsync(prepared, cancellationToken).ConfigureAwait(false);
        return new CircleFilesHostApplyResult(status, prepared.PublicPlan);
    }

    internal ValueTask<WindowsCircleFilesHelperPlan> PrepareForHelperAsync(
        CircleFilesHostRequest request,
        CancellationToken cancellationToken) => PrepareAsync(request, cancellationToken);

    internal ValueTask<WindowsCircleFilesHelperPlan> PrepareForRemovalAsync(
        CircleFilesHostRequest request,
        CancellationToken cancellationToken) =>
        PrepareAsync(
            request,
            cancellationToken,
            requireReadiness: false,
            allowOwnedContent: true);

    private async ValueTask<WindowsCircleFilesHelperPlan> PrepareAsync(
        CircleFilesHostRequest request,
        CancellationToken cancellationToken,
        bool requireReadiness = true,
        bool allowOwnedContent = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCanonicalId(request.CircleId, "Circle");
        ValidateCanonicalId(request.ContributionId, "Contribution");
        ValidateCanonicalId(request.ProviderId, "Provider");
        ValidateCanonicalId(request.NodeId, "Node");
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 100)
        {
            throw InvalidPath("The contribution display name is invalid.");
        }

        if (request.AuthorizationDigest.Length != 64
            || request.AuthorizationDigest.Any(character =>
                !Uri.IsHexDigit(character) || char.IsUpper(character)))
        {
            throw new CircleFilesHostingException(
                "hosting_authorization_invalid",
                "The contribution authorization binding is invalid.");
        }

        if (requireReadiness)
        {
            var report = await readiness.InspectAsync(cancellationToken).ConfigureAwait(false);
            if (report.Status != CircleFilesReadinessStatus.Ready)
            {
                throw new CircleFilesHostingException(
                    "hosting_prerequisites_not_ready",
                    "Windows SMB hosting prerequisites are not ready.");
            }
        }

        var fullPath = ValidatePath(request.FolderPath);
        var ownershipId = HashCanonical(
            "balls-windows-smb-ownership-v1",
            request.CircleId,
            request.ContributionId,
            request.ProviderId,
            request.NodeId,
            fullPath.ToUpperInvariant());
        var markerContent = WindowsCircleFilesOwnershipMarker.Create(
            ownershipId,
            request,
            fullPath,
            environment.CurrentUserSid);
        var providerSuffix = request.ProviderId.Replace("-", string.Empty, StringComparison.Ordinal)[..12];
        var shareName = $"balls-{providerSuffix}";
        var firewallRuleName = $"Balls-SMB-{request.ProviderId.Replace("-", string.Empty, StringComparison.Ordinal)}";
        var planId = HashCanonical(
            "balls-windows-smb-host-plan-v1",
            request.CircleId,
            request.ContributionId,
            request.ProviderId,
            request.NodeId,
            request.AuthorizationDigest,
            fullPath.ToUpperInvariant(),
            shareName,
            firewallRuleName,
            ownershipId,
            environment.CurrentUserSid);
        var targetExists = environment.DirectoryExists(fullPath);
        if (targetExists)
        {
            var entries = environment.EnumerateEntries(fullPath);
            var markerPath = Path.Combine(fullPath, WindowsCircleFilesOwnershipMarker.FileName);
            var marker = environment.ReadAllText(markerPath);
            if (marker is null)
            {
                var journalPath = Path.Combine(
                    fullPath,
                    WindowsCircleFilesSystemOperations.JournalFileName);
                var journalEntry = entries.SingleOrDefault(entry =>
                    Path.GetFileName(entry).Equals(
                        WindowsCircleFilesSystemOperations.JournalFileName,
                        StringComparison.OrdinalIgnoreCase));
                if (journalEntry is not null
                    && !WindowsCircleFilesSystemOperations.IsOwnedJournalContent(
                        environment.ReadAllText(journalPath),
                        ownershipId,
                        planId,
                        fullPath,
                        environment.CurrentUserSid))
                {
                    throw new CircleFilesHostingException(
                        "hosting_ownership_collision",
                        "The selected folder contains a different Balls operation journal.");
                }
                if (!allowOwnedContent
                    && entries.Any(entry =>
                        WindowsCircleFilesSystemOperations.IsReservedMetadataPath(entry)
                        && !Path.GetFileName(entry).Equals(
                            WindowsCircleFilesSystemOperations.JournalFileName,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    throw new CircleFilesHostingException(
                        "hosting_ownership_collision",
                        "The selected folder contains reserved Balls hosting metadata.");
                }
            }
            else if (!string.Equals(marker, markerContent, StringComparison.Ordinal))
            {
                throw new CircleFilesHostingException(
                    "hosting_ownership_collision",
                    "The selected folder has a different ownership marker.");
            }
        }

        var plan = new CircleFilesHostPlan(
            CircleFilesHostingContract.Version,
            planId,
            CircleFilesReadinessProviders.WindowsSmb311,
            fullPath,
            shareName,
            firewallRuleName,
            ownershipId,
            targetExists,
            [
                "Keep every existing file in place and apply the exact folder's protected hosting ACL.",
                "Write the exact Balls ownership marker.",
                "Create the encrypted SMB share for the current Owner only.",
                "Allow SMB only on Private networks from the local subnet.",
            ]);
        return new WindowsCircleFilesHelperPlan(plan, request with { FolderPath = fullPath }, environment.CurrentUserSid);
    }

    private string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Length > 220
            || path.StartsWith("\\\\", StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(path))
        {
            throw InvalidPath("The hosting folder must be an absolute local path.");
        }

        string fullPath;
        try
        {
            fullPath = environment.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            throw InvalidPath("The hosting folder path is invalid.");
        }

        var root = environment.GetPathRoot(fullPath);
        if (string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)
            || !environment.IsFixedLocalDrive(root)
            || environment.FileExists(fullPath)
            || environment.HasReparsePointInExistingPath(fullPath)
            || Path.GetDirectoryName(fullPath) is not { } parent
            || !environment.DirectoryExists(parent)
            || environment.RefusedRoots.Any(refused => IsAtOrBelow(fullPath, refused)))
        {
            throw InvalidPath("The hosting folder must be a dedicated local folder outside protected or general-purpose locations.");
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar);
    }

    private static bool IsAtOrBelow(string path, string root)
    {
        var normalized = root.TrimEnd(Path.DirectorySeparatorChar);
        return path.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateCanonicalId(string value, string label)
    {
        if (!Guid.TryParseExact(value, "D", out var parsed)
            || parsed == Guid.Empty
            || !string.Equals(parsed.ToString("D"), value, StringComparison.Ordinal))
        {
            throw new CircleFilesHostingException(
                "hosting_authorization_invalid",
                $"The {label.ToLowerInvariant()} hosting binding is invalid.");
        }
    }

    private static CircleFilesHostingException InvalidPath(string message) =>
        new("hosting_path_invalid", message);

    internal static string HashCanonical(params string[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(bytes.Length)));
            hash.AppendData(bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

internal static class WindowsCircleFilesOwnershipMarker
{
    internal const string FileName = ".balls-owned-v1.json";

    internal static string Create(
        string ownershipId,
        CircleFilesHostRequest request,
        string fullPath,
        string ownerSid) =>
        JsonSerializer.Serialize(
            new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["authorizationDigest"] = request.AuthorizationDigest,
                ["circleId"] = request.CircleId,
                ["contractVersion"] = CircleFilesHostingContract.Version,
                ["contributionId"] = request.ContributionId,
                ["folderPath"] = fullPath,
                ["nodeId"] = request.NodeId,
                ["ownerSid"] = ownerSid,
                ["ownershipId"] = ownershipId,
                ["providerId"] = request.ProviderId,
            }) + "\n";
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsCircleFilesPathEnvironment : IWindowsCircleFilesPathEnvironment
{
    private readonly string? authenticatedUserSid;

    internal WindowsCircleFilesPathEnvironment(string? authenticatedUserSid = null)
    {
        this.authenticatedUserSid = authenticatedUserSid;
    }

    public string CurrentUserSid => authenticatedUserSid
        ?? WindowsIdentity.GetCurrent().User?.Value
        ?? throw new CircleFilesHostingException(
            "hosting_identity_unavailable",
            "The current Windows account identity is unavailable.");

    public IReadOnlyList<string> RefusedRoots
    {
        get
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var profilesRoot = Directory.GetParent(userProfile)?.FullName ?? userProfile;
            return
            [
                profilesRoot,
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ];
        }
    }

    public string GetFullPath(string path) => Path.GetFullPath(path);

    public string GetPathRoot(string path) => Path.GetPathRoot(path)
        ?? throw new ArgumentException("The path has no root.", nameof(path));

    public bool IsFixedLocalDrive(string root) => new DriveInfo(root).DriveType == DriveType.Fixed;

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> EnumerateEntries(string path) =>
        Directory.EnumerateFileSystemEntries(path).ToArray();

    public string? ReadAllText(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    public bool HasReparsePointInExistingPath(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            if (!current.Exists)
            {
                continue;
            }

            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsElevatedCircleFilesHelperClient : IWindowsCircleFilesHelperClient
{
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(2);
    private const int MaximumMessageBytes = 64 * 1024;

    public async ValueTask<CircleFilesHostApplyStatus> ApplyAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var helperPath = Path.Combine(AppContext.BaseDirectory, "balls-windows-helper.exe");
        if (!File.Exists(helperPath))
        {
            throw new CircleFilesHostingException(
                "hosting_helper_unavailable",
                "The Windows Circle Files helper is unavailable.");
        }

        var pipeName = $"balls-host-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))}";
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var ownerSid = new SecurityIdentifier(plan.OwnerSid);
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
            inBufferSize: 0,
            outBufferSize: 0,
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
                    "The Windows Circle Files helper could not start.");
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new CircleFilesHostingException(
                "hosting_consent_cancelled",
                "The Windows administrator approval was cancelled.");
        }
        catch (Win32Exception)
        {
            throw new CircleFilesHostingException(
                "hosting_helper_unavailable",
                "The Windows Circle Files helper could not start.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ApprovalTimeout);
        try
        {
            await WindowsCircleFilesHelperProcess.WaitForConnectionAsync(
                pipe,
                helper,
                timeout.Token).ConfigureAwait(false);
            if (!WindowsNamedPipeProcessIdentity.TryGetClientProcessId(pipe, out var clientPid)
                || clientPid != helper.Id)
            {
                throw new CircleFilesHostingException(
                    "hosting_helper_authentication_failed",
                    "The elevated helper connection could not be authenticated.");
            }

            await WindowsCircleFilesHelperProtocol.WriteAsync(
                pipe,
                new WindowsCircleFilesHelperEnvelope("host", plan, null),
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

            return response.Status switch
            {
                "applied" => CircleFilesHostApplyStatus.Applied,
                "already-applied" => CircleFilesHostApplyStatus.AlreadyApplied,
                _ => throw new CircleFilesHostingException(
                    "hosting_helper_invalid_response",
                    "The Windows Circle Files helper returned an invalid response."),
            };
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
                "The Windows Circle Files helper returned an invalid response.");
        }
    }
}

internal sealed record WindowsCircleFilesHelperResponse(
    string? Status,
    string? ErrorCode,
    string Message,
    int OpenSessionCount = 0);

internal sealed record WindowsCircleFilesHelperEnvelope(
    string Operation,
    WindowsCircleFilesHelperPlan? Host,
    WindowsCircleFilesGrantHelperPlan? Grant,
    WindowsCircleFilesGrantCleanupHelperPlan? GrantCleanup = null,
    WindowsCircleFilesHostRemovalHelperPlan? HostRemoval = null);

internal static class WindowsCircleFilesHelperProcess
{
    internal static async Task WaitForConnectionAsync(
        NamedPipeServerStream pipe,
        Process helper,
        CancellationToken cancellationToken)
    {
        var connection = pipe.WaitForConnectionAsync(cancellationToken);
        var exit = helper.WaitForExitAsync(cancellationToken);
        var completed = await Task.WhenAny(connection, exit).ConfigureAwait(false);
        if (completed == exit)
        {
            await exit.ConfigureAwait(false);
            if (!pipe.IsConnected)
            {
                throw new CircleFilesHostingException(
                    "hosting_helper_unavailable",
                    "The Windows Circle Files helper exited before connecting.");
            }
        }

        await connection.ConfigureAwait(false);
    }
}

internal static class WindowsCircleFilesHelperProtocol
{
    internal static async ValueTask WriteAsync<T>(
        Stream stream,
        T value,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length == 0 || bytes.Length > maximumBytes)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new InvalidDataException("The helper message is outside its size limit.");
        }

        var length = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(bytes.Length));
        try
        {
            await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    internal static async ValueTask<T> ReadAsync<T>(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        var length = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes));
        if (length <= 0 || length > maximumBytes)
        {
            throw new InvalidDataException("The helper message is outside its size limit.");
        }

        var bytes = new byte[length];
        try
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(bytes)
                ?? throw new InvalidDataException("The helper message is invalid.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

internal static partial class WindowsNamedPipeProcessIdentity
{
    internal static bool TryGetClientProcessId(PipeStream pipe, out int processId)
    {
        if (GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var value) && value <= int.MaxValue)
        {
            processId = (int)value;
            return true;
        }

        processId = 0;
        return false;
    }

    internal static bool TryGetServerProcessId(PipeStream pipe, out int processId)
    {
        if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var value) && value <= int.MaxValue)
        {
            processId = (int)value;
            return true;
        }

        processId = 0;
        return false;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(
        Microsoft.Win32.SafeHandles.SafePipeHandle pipe,
        out uint clientProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(
        Microsoft.Win32.SafeHandles.SafePipeHandle pipe,
        out uint serverProcessId);
}

[SupportedOSPlatform("windows")]
public static class WindowsCircleFilesHelperCommand
{
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(2);
    private const int MaximumMessageBytes = 64 * 1024;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length != 4
            || args[0] != "--pipe-name"
            || string.IsNullOrWhiteSpace(args[1])
            || args[1].Length > 128
            || args[2] != "--server-pid"
            || !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out var serverPid)
            || serverPid <= 0
            || !new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator))
        {
            return 2;
        }

        if (!WindowsProcessIdentity.TryGetExpectedDaemonUserSid(serverPid, out var daemonUserSid))
        {
            return 3;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(MaximumLifetime);
        var helperToken = lifetime.Token;
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                args[1],
                PipeDirection.InOut,
                PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(10_000, helperToken).ConfigureAwait(false);
            if (!WindowsNamedPipeProcessIdentity.TryGetServerProcessId(pipe, out var actualServerPid)
                || actualServerPid != serverPid)
            {
                return 3;
            }

            try
            {
                var envelope = await WindowsCircleFilesHelperProtocol.ReadAsync<WindowsCircleFilesHelperEnvelope>(
                    pipe,
                    MaximumMessageBytes,
                    helperToken).ConfigureAwait(false);
                try
                {
                    if (envelope.Operation == "host" && envelope.Host is { } received)
                    {
                        if (received.OwnerSid != daemonUserSid)
                        {
                            await WriteErrorAsync(pipe, "hosting_helper_authentication_failed", helperToken)
                                .ConfigureAwait(false);
                            return 4;
                        }

                        WindowsCircleFilesHostAuthorizationVerifier.Validate(received.Request);
                        var verifier = CreateHostVerifier(daemonUserSid);
                        var recomputed = await verifier.PrepareForHelperAsync(received.Request, helperToken)
                            .ConfigureAwait(false);
                        if (!PlansEqual(received, recomputed))
                        {
                            await WriteErrorAsync(pipe, "hosting_helper_authentication_failed", helperToken)
                                .ConfigureAwait(false);
                            return 4;
                        }

                        var status = await new WindowsCircleFilesOperation(new WindowsCircleFilesSystemOperations())
                            .ExecuteAsync(recomputed, helperToken).ConfigureAwait(false);
                        await WriteSuccessAsync(
                            pipe,
                            status == CircleFilesHostApplyStatus.Applied,
                            "The dedicated Circle Files host is ready.",
                            helperToken).ConfigureAwait(false);
                        return 0;
                    }

                    if (envelope.Operation == "grant" && envelope.Grant is { } grant)
                    {
                        try
                        {
                            if (grant.OwnerSid != daemonUserSid || grant.Secret.Length is < 24 or > 128)
                            {
                                await WriteErrorAsync(pipe, "hosting_helper_authentication_failed", helperToken)
                                    .ConfigureAwait(false);
                                return 4;
                            }
                            WindowsCircleFilesGrantAuthorizationVerifier.Validate(grant.Request);
                            var verifier = new WindowsCircleFilesGrantCredentialProvisioner(
                                CreateHostVerifier(daemonUserSid),
                                new RejectNestedGrantHelper());
                            var recomputed = await verifier.PrepareForHelperAsync(
                                grant.Request,
                                grant.Secret,
                                helperToken).ConfigureAwait(false);
                            if (!GrantPlansEqual(grant, recomputed))
                            {
                                await WriteErrorAsync(pipe, "hosting_helper_authentication_failed", helperToken)
                                    .ConfigureAwait(false);
                                return 4;
                            }

                            var status = await new WindowsCircleFilesGrantOperation(
                                    new WindowsCircleFilesGrantSystemOperations())
                                .ExecuteAsync(recomputed, helperToken).ConfigureAwait(false);
                            await WriteSuccessAsync(
                                pipe,
                                status == CircleFilesGrantCredentialApplyStatus.Applied,
                                "The limited Windows Member credential is ready.",
                                helperToken).ConfigureAwait(false);
                            return 0;
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(grant.Secret);
                        }
                    }

                    if (envelope.Operation == "grant-remove"
                        && envelope.GrantCleanup is { } grantCleanup)
                    {
                        try
                        {
                            if (grantCleanup.OwnerSid != daemonUserSid
                                || grantCleanup.Secret.Length is < 24 or > 128)
                            {
                                await WriteErrorAsync(
                                    pipe,
                                    "hosting_helper_authentication_failed",
                                    helperToken).ConfigureAwait(false);
                                return 4;
                            }

                            WindowsCircleFilesGrantAuthorizationVerifier.ValidateCleanup(
                                grantCleanup.Request);
                            var hostVerifier = CreateHostVerifier(daemonUserSid);
                            var grantVerifier = new WindowsCircleFilesGrantCredentialProvisioner(
                                hostVerifier,
                                new RejectNestedGrantHelper());
                            var verifier = new WindowsCircleFilesLifecycleManager(
                                hostVerifier,
                                grantVerifier,
                                new RejectLifecycleHelper());
                            var recomputed = await verifier.PrepareGrantForHelperAsync(
                                grantCleanup.Request,
                                grantCleanup.Secret,
                                grantCleanup.TerminateOpenSessions,
                                helperToken).ConfigureAwait(false);
                            if (!GrantCleanupPlansEqual(grantCleanup, recomputed))
                            {
                                await WriteErrorAsync(
                                    pipe,
                                    "hosting_helper_authentication_failed",
                                    helperToken).ConfigureAwait(false);
                                return 4;
                            }

                            var operations = new WindowsCircleFilesGrantSystemOperations();
                            var result = await new WindowsCircleFilesGrantRemovalOperation(
                                    operations,
                                    operations)
                                .ExecuteAsync(
                                    recomputed.GrantPlan,
                                    recomputed.TerminateOpenSessions,
                                    helperToken).ConfigureAwait(false);
                            await WriteCleanupAsync(pipe, result, helperToken).ConfigureAwait(false);
                            return 0;
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(grantCleanup.Secret);
                        }
                    }

                    if (envelope.Operation == "host-remove"
                        && envelope.HostRemoval is { } hostRemoval)
                    {
                        if (hostRemoval.OwnerSid != daemonUserSid)
                        {
                            await WriteErrorAsync(
                                pipe,
                                "hosting_helper_authentication_failed",
                                helperToken).ConfigureAwait(false);
                            return 4;
                        }

                        WindowsCircleFilesHostAuthorizationVerifier.Validate(hostRemoval.Request);
                        var hostVerifier = CreateHostVerifier(daemonUserSid);
                        var grantVerifier = new WindowsCircleFilesGrantCredentialProvisioner(
                            hostVerifier,
                            new RejectNestedGrantHelper());
                        var verifier = new WindowsCircleFilesLifecycleManager(
                            hostVerifier,
                            grantVerifier,
                            new RejectLifecycleHelper());
                        var recomputed = await verifier.PrepareHostForHelperAsync(
                            hostRemoval.Request,
                            hostRemoval.TerminateOpenSessions,
                            helperToken).ConfigureAwait(false);
                        if (!HostRemovalPlansEqual(hostRemoval, recomputed))
                        {
                            await WriteErrorAsync(
                                pipe,
                                "hosting_helper_authentication_failed",
                                helperToken).ConfigureAwait(false);
                            return 4;
                        }

                        var operations = new WindowsCircleFilesSystemOperations();
                        var result = await new WindowsCircleFilesHostRemovalOperation(
                                operations,
                                operations)
                            .ExecuteAsync(
                                recomputed.HostPlan,
                                recomputed.TerminateOpenSessions,
                                helperToken).ConfigureAwait(false);
                        await WriteCleanupAsync(pipe, result, helperToken).ConfigureAwait(false);
                        return 0;
                    }

                    await WriteErrorAsync(pipe, "hosting_helper_authentication_failed", helperToken)
                        .ConfigureAwait(false);
                    return 4;
                }
                finally
                {
                    ZeroGrantSecret(envelope);
                }
            }
            catch (CircleFilesHostingException exception)
            {
                await WriteErrorAsync(
                    pipe,
                    exception.Code,
                    helperToken,
                    SafeErrorMessage(exception)).ConfigureAwait(false);
                return 6;
            }
        }
        catch (OperationCanceledException)
        {
            return 7;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return 5;
        }
    }

    internal static void ZeroGrantSecret(WindowsCircleFilesHelperEnvelope envelope)
    {
        if (envelope.Grant is { } sensitiveGrant)
        {
            CryptographicOperations.ZeroMemory(sensitiveGrant.Secret);
        }
        if (envelope.GrantCleanup is { } sensitiveCleanup)
        {
            CryptographicOperations.ZeroMemory(sensitiveCleanup.Secret);
        }
    }

    internal static bool PlansEqual(
        WindowsCircleFilesHelperPlan received,
        WindowsCircleFilesHelperPlan recomputed) =>
        received.OwnerSid == recomputed.OwnerSid
        && HostRequestsEqual(received.Request, recomputed.Request)
        && received.PublicPlan.ContractVersion == recomputed.PublicPlan.ContractVersion
        && received.PublicPlan.PlanId == recomputed.PublicPlan.PlanId
        && received.PublicPlan.Provider == recomputed.PublicPlan.Provider
        && received.PublicPlan.FolderPath.Equals(recomputed.PublicPlan.FolderPath, StringComparison.OrdinalIgnoreCase)
        && received.PublicPlan.ShareName == recomputed.PublicPlan.ShareName
        && received.PublicPlan.FirewallRuleName == recomputed.PublicPlan.FirewallRuleName
        && received.PublicPlan.OwnershipId == recomputed.PublicPlan.OwnershipId
        && received.PublicPlan.TargetExists == recomputed.PublicPlan.TargetExists
        && received.PublicPlan.Actions.SequenceEqual(recomputed.PublicPlan.Actions, StringComparer.Ordinal);

    internal static bool GrantPlansEqual(
        WindowsCircleFilesGrantHelperPlan received,
        WindowsCircleFilesGrantHelperPlan recomputed) =>
        received.OwnerSid == recomputed.OwnerSid
        && GrantRequestsEqual(received.Request, recomputed.Request)
        && PlansEqual(received.HostPlan, recomputed.HostPlan)
        && received.PublicPlan.ContractVersion == recomputed.PublicPlan.ContractVersion
        && received.PublicPlan.PlanId == recomputed.PublicPlan.PlanId
        && received.PublicPlan.Provider == recomputed.PublicPlan.Provider
        && received.PublicPlan.FolderPath.Equals(recomputed.PublicPlan.FolderPath, StringComparison.OrdinalIgnoreCase)
        && received.PublicPlan.ShareName == recomputed.PublicPlan.ShareName
        && received.PublicPlan.AccountName == recomputed.PublicPlan.AccountName
        && received.PublicPlan.OwnershipId == recomputed.PublicPlan.OwnershipId
        && received.PublicPlan.Access == recomputed.PublicPlan.Access
        && received.PublicPlan.Generation == recomputed.PublicPlan.Generation
        && received.PublicPlan.Actions.SequenceEqual(recomputed.PublicPlan.Actions, StringComparer.Ordinal)
        && CryptographicOperations.FixedTimeEquals(received.Secret, recomputed.Secret);

    internal static bool GrantCleanupPlansEqual(
        WindowsCircleFilesGrantCleanupHelperPlan received,
        WindowsCircleFilesGrantCleanupHelperPlan recomputed) =>
        received.OwnerSid == recomputed.OwnerSid
        && received.TerminateOpenSessions == recomputed.TerminateOpenSessions
        && GrantCleanupRequestsEqual(received.Request, recomputed.Request)
        && GrantPlansEqual(received.GrantPlan, recomputed.GrantPlan)
        && received.PublicPlan.ContractVersion == recomputed.PublicPlan.ContractVersion
        && received.PublicPlan.PlanId == recomputed.PublicPlan.PlanId
        && received.PublicPlan.Provider == recomputed.PublicPlan.Provider
        && received.PublicPlan.FolderPath.Equals(
            recomputed.PublicPlan.FolderPath,
            StringComparison.OrdinalIgnoreCase)
        && received.PublicPlan.ShareName == recomputed.PublicPlan.ShareName
        && received.PublicPlan.AccountName == recomputed.PublicPlan.AccountName
        && received.PublicPlan.OwnershipId == recomputed.PublicPlan.OwnershipId
        && received.PublicPlan.Generation == recomputed.PublicPlan.Generation
        && received.PublicPlan.Actions.SequenceEqual(recomputed.PublicPlan.Actions, StringComparer.Ordinal)
        && CryptographicOperations.FixedTimeEquals(received.Secret, recomputed.Secret);

    internal static bool HostRemovalPlansEqual(
        WindowsCircleFilesHostRemovalHelperPlan received,
        WindowsCircleFilesHostRemovalHelperPlan recomputed) =>
        received.OwnerSid == recomputed.OwnerSid
        && received.TerminateOpenSessions == recomputed.TerminateOpenSessions
        && HostRequestsEqual(received.Request, recomputed.Request)
        && PlansEqual(received.HostPlan, recomputed.HostPlan)
        && received.PublicPlan.ContractVersion == recomputed.PublicPlan.ContractVersion
        && received.PublicPlan.PlanId == recomputed.PublicPlan.PlanId
        && received.PublicPlan.Provider == recomputed.PublicPlan.Provider
        && received.PublicPlan.FolderPath.Equals(
            recomputed.PublicPlan.FolderPath,
            StringComparison.OrdinalIgnoreCase)
        && received.PublicPlan.ShareName == recomputed.PublicPlan.ShareName
        && received.PublicPlan.FirewallRuleName == recomputed.PublicPlan.FirewallRuleName
        && received.PublicPlan.OwnershipId == recomputed.PublicPlan.OwnershipId
        && received.PublicPlan.Actions.SequenceEqual(recomputed.PublicPlan.Actions, StringComparer.Ordinal);

    private static bool GrantCleanupRequestsEqual(
        CircleFilesGrantCleanupRequest received,
        CircleFilesGrantCleanupRequest recomputed) =>
        GrantRequestsEqual(received.Grant, recomputed.Grant)
        && received.Revocation.RequestId == recomputed.Revocation.RequestId
        && received.Revocation.CircleId == recomputed.Revocation.CircleId
        && received.Revocation.ContributionId == recomputed.Revocation.ContributionId
        && received.Revocation.GrantId == recomputed.Revocation.GrantId
        && received.Revocation.RevokedGeneration == recomputed.Revocation.RevokedGeneration
        && received.Revocation.AuthorizationDigest == recomputed.Revocation.AuthorizationDigest
        && ProofsEqual(received.Revocation.Authorization, recomputed.Revocation.Authorization);

    private static bool GrantRequestsEqual(
        CircleFilesGrantCredentialRequest received,
        CircleFilesGrantCredentialRequest recomputed) =>
        HostRequestsEqual(received.Host, recomputed.Host)
        && received.GrantId == recomputed.GrantId
        && received.MemberId == recomputed.MemberId
        && received.Access == recomputed.Access
        && received.Generation == recomputed.Generation
        && received.AuthorizationDigest == recomputed.AuthorizationDigest
        && ProofsEqual(received.Authorization, recomputed.Authorization);

    private static bool HostRequestsEqual(
        CircleFilesHostRequest received,
        CircleFilesHostRequest recomputed) =>
        received.CircleId == recomputed.CircleId
        && received.ContributionId == recomputed.ContributionId
        && received.ProviderId == recomputed.ProviderId
        && received.NodeId == recomputed.NodeId
        && received.DisplayName == recomputed.DisplayName
        && received.FolderPath.Equals(recomputed.FolderPath, StringComparison.OrdinalIgnoreCase)
        && received.AuthorizationDigest == recomputed.AuthorizationDigest
        && received.Authorization is not null
        && recomputed.Authorization is not null
        && ProofsEqual(received.Authorization, recomputed.Authorization);

    private static bool ProofsEqual(
        CircleFilesHostAuthorizationProof received,
        CircleFilesHostAuthorizationProof recomputed) =>
        CryptographicOperations.FixedTimeEquals(received.Transcript, recomputed.Transcript)
        && CryptographicOperations.FixedTimeEquals(received.MemberSignature, recomputed.MemberSignature)
        && CryptographicOperations.FixedTimeEquals(
            received.CircleAuthoritySignature,
            recomputed.CircleAuthoritySignature)
        && CredentialsEqual(received.MemberCredential, recomputed.MemberCredential)
        && CredentialsEqual(received.CircleAuthorityCredential, recomputed.CircleAuthorityCredential);

    private static bool CredentialsEqual(
        CircleFilesHostPublicCredential received,
        CircleFilesHostPublicCredential recomputed) =>
        received.Role == recomputed.Role
        && received.Algorithm == recomputed.Algorithm
        && received.KeyId == recomputed.KeyId
        && CryptographicOperations.FixedTimeEquals(
            received.SubjectPublicKeyInfo,
            recomputed.SubjectPublicKeyInfo);

    private static WindowsCircleFilesHostProvisioner CreateHostVerifier(string daemonUserSid) =>
        new(
            new WindowsSmbReadinessInspector(),
            new WindowsCircleFilesPathEnvironment(daemonUserSid),
            new RejectNestedHelper());

    private static async Task WriteSuccessAsync(
        Stream pipe,
        bool applied,
        string message,
        CancellationToken cancellationToken) =>
        await WindowsCircleFilesHelperProtocol.WriteAsync(
            pipe,
            new WindowsCircleFilesHelperResponse(
                applied ? "applied" : "already-applied",
                null,
                message),
            MaximumMessageBytes,
            cancellationToken).ConfigureAwait(false);

    private static async Task WriteErrorAsync(
        Stream pipe,
        string code,
        CancellationToken cancellationToken,
        string message = "The Windows Circle Files helper refused the operation.") =>
        await WindowsCircleFilesHelperProtocol.WriteAsync(
            pipe,
            new WindowsCircleFilesHelperResponse(
                null,
                code,
                message),
            MaximumMessageBytes,
            cancellationToken).ConfigureAwait(false);

    internal static string SafeErrorMessage(CircleFilesHostingException exception) =>
        exception.Code == "grant_apply_failed"
        && WindowsCircleFilesGrantOperation.IsSafeFailureMessage(exception.Message)
            ? exception.Message
            : "The Windows Circle Files helper refused the operation.";

    private static async Task WriteCleanupAsync(
        Stream pipe,
        CircleFilesCleanupExecution result,
        CancellationToken cancellationToken) =>
        await WindowsCircleFilesHelperProtocol.WriteAsync(
            pipe,
            new WindowsCircleFilesHelperResponse(
                result.Status switch
                {
                    CircleFilesCleanupStatus.Removed => "removed",
                    CircleFilesCleanupStatus.AlreadyRemoved => "already-removed",
                    CircleFilesCleanupStatus.Busy => "busy",
                    CircleFilesCleanupStatus.Partial => "partial",
                    _ => throw new ArgumentOutOfRangeException(nameof(result)),
                },
                null,
                "The exact Circle Files cleanup operation completed.",
                result.OpenSessionCount),
            MaximumMessageBytes,
            cancellationToken).ConfigureAwait(false);

    private sealed class RejectNestedHelper : IWindowsCircleFilesHelperClient
    {
        public ValueTask<CircleFilesHostApplyStatus> ApplyAsync(
            WindowsCircleFilesHelperPlan plan,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<CircleFilesHostApplyStatus>(new InvalidOperationException());
    }

    private sealed class RejectNestedGrantHelper : IWindowsCircleFilesGrantHelperClient
    {
        public ValueTask<CircleFilesGrantCredentialApplyStatus> ApplyAsync(
            WindowsCircleFilesGrantHelperPlan plan,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<CircleFilesGrantCredentialApplyStatus>(new InvalidOperationException());
    }

    private sealed class RejectLifecycleHelper : IWindowsCircleFilesLifecycleHelperClient
    {
        public ValueTask<CircleFilesCleanupExecution> RemoveGrantAsync(
            WindowsCircleFilesGrantCleanupHelperPlan plan,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<CircleFilesCleanupExecution>(new InvalidOperationException());

        public ValueTask<CircleFilesCleanupExecution> RemoveHostAsync(
            WindowsCircleFilesHostRemovalHelperPlan plan,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<CircleFilesCleanupExecution>(new InvalidOperationException());
    }
}
