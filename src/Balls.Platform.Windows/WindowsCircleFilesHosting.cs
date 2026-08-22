using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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

    private async ValueTask<WindowsCircleFilesHelperPlan> PrepareAsync(
        CircleFilesHostRequest request,
        CancellationToken cancellationToken)
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

        var report = await readiness.InspectAsync(cancellationToken).ConfigureAwait(false);
        if (report.Status != CircleFilesReadinessStatus.Ready)
        {
            throw new CircleFilesHostingException(
                "hosting_prerequisites_not_ready",
                "Windows SMB hosting prerequisites are not ready.");
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
                var recoverable = entries.Count == 1
                    && Path.GetFileName(entries[0]).Equals(
                        WindowsCircleFilesSystemOperations.JournalFileName,
                        StringComparison.OrdinalIgnoreCase)
                    && WindowsCircleFilesSystemOperations.IsOwnedJournalContent(
                        environment.ReadAllText(journalPath),
                        ownershipId,
                        planId,
                        fullPath,
                        environment.CurrentUserSid);
                if (entries.Count != 0 && !recoverable)
                {
                    throw new CircleFilesHostingException(
                        "hosting_folder_not_empty",
                        "The selected folder must be new or empty.");
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
                "Create or dedicate the exact empty folder and apply its protected ACL.",
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
    public string CurrentUserSid => WindowsIdentity.GetCurrent().User?.Value
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
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
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
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (!WindowsNamedPipeProcessIdentity.TryGetClientProcessId(pipe, out var clientPid)
                || clientPid != helper.Id)
            {
                throw new CircleFilesHostingException(
                    "hosting_helper_authentication_failed",
                    "The elevated helper connection could not be authenticated.");
            }

            await WindowsCircleFilesHelperProtocol.WriteAsync(
                pipe,
                plan,
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
    string Message);

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
            throw new InvalidDataException("The helper message is outside its size limit.");
        }

        var length = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(bytes.Length));
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
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
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes)
            ?? throw new InvalidDataException("The helper message is invalid.");
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

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                args[1],
                PipeDirection.InOut,
                PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(10_000, cancellationToken).ConfigureAwait(false);
            if (!WindowsNamedPipeProcessIdentity.TryGetServerProcessId(pipe, out var actualServerPid)
                || actualServerPid != serverPid)
            {
                return 3;
            }

            try
            {
                var received = await WindowsCircleFilesHelperProtocol.ReadAsync<WindowsCircleFilesHelperPlan>(
                    pipe,
                    MaximumMessageBytes,
                    cancellationToken).ConfigureAwait(false);
                var verifier = new WindowsCircleFilesHostProvisioner(
                    new WindowsSmbReadinessInspector(),
                    new WindowsCircleFilesPathEnvironment(),
                    new RejectNestedHelper());
                var recomputed = await verifier.PrepareForHelperAsync(received.Request, cancellationToken)
                    .ConfigureAwait(false);
                if (!PlansEqual(received, recomputed))
                {
                    await WriteErrorAsync(pipe, "hosting_helper_authentication_failed", cancellationToken)
                        .ConfigureAwait(false);
                    return 4;
                }

                var status = await new WindowsCircleFilesOperation(new WindowsCircleFilesSystemOperations())
                    .ExecuteAsync(recomputed, cancellationToken).ConfigureAwait(false);
                await WindowsCircleFilesHelperProtocol.WriteAsync(
                    pipe,
                    new WindowsCircleFilesHelperResponse(
                        status == CircleFilesHostApplyStatus.Applied ? "applied" : "already-applied",
                        null,
                        "The dedicated Circle Files host is ready."),
                    MaximumMessageBytes,
                    cancellationToken).ConfigureAwait(false);
                return 0;
            }
            catch (CircleFilesHostingException exception)
            {
                await WriteErrorAsync(pipe, exception.Code, cancellationToken).ConfigureAwait(false);
                return 6;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return 5;
        }
    }

    private static bool PlansEqual(
        WindowsCircleFilesHelperPlan received,
        WindowsCircleFilesHelperPlan recomputed) =>
        received.OwnerSid == recomputed.OwnerSid
        && received.Request == recomputed.Request
        && received.PublicPlan.ContractVersion == recomputed.PublicPlan.ContractVersion
        && received.PublicPlan.PlanId == recomputed.PublicPlan.PlanId
        && received.PublicPlan.Provider == recomputed.PublicPlan.Provider
        && received.PublicPlan.FolderPath.Equals(recomputed.PublicPlan.FolderPath, StringComparison.OrdinalIgnoreCase)
        && received.PublicPlan.ShareName == recomputed.PublicPlan.ShareName
        && received.PublicPlan.FirewallRuleName == recomputed.PublicPlan.FirewallRuleName
        && received.PublicPlan.OwnershipId == recomputed.PublicPlan.OwnershipId
        && received.PublicPlan.TargetExists == recomputed.PublicPlan.TargetExists
        && received.PublicPlan.Actions.SequenceEqual(recomputed.PublicPlan.Actions, StringComparer.Ordinal);

    private static async Task WriteErrorAsync(
        Stream pipe,
        string code,
        CancellationToken cancellationToken) =>
        await WindowsCircleFilesHelperProtocol.WriteAsync(
            pipe,
            new WindowsCircleFilesHelperResponse(
                null,
                code,
                "The Windows Circle Files helper refused the operation."),
            MaximumMessageBytes,
            cancellationToken).ConfigureAwait(false);

    private sealed class RejectNestedHelper : IWindowsCircleFilesHelperClient
    {
        public ValueTask<CircleFilesHostApplyStatus> ApplyAsync(
            WindowsCircleFilesHelperPlan plan,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<CircleFilesHostApplyStatus>(new InvalidOperationException());
    }
}
