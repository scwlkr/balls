using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Balls.Platform;
using Microsoft.Win32;

namespace Balls.Platform.Windows;

internal sealed class WindowsCircleFilesStoredCredential(
    string target,
    string accountName,
    string ownershipId,
    byte[] secret) : IDisposable
{
    public string Target { get; } = target;
    public string AccountName { get; } = accountName;
    public string OwnershipId { get; } = ownershipId;
    public ReadOnlyMemory<byte> Secret => secret;

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(secret);
        secret = [];
    }

    public override string ToString() => "Windows Circle Files credential (redacted)";
}

internal sealed record WindowsCircleFilesStoredLabel(string FriendlyName, string OwnershipId);

internal interface IWindowsCircleFilesMappingOperations
{
    IReadOnlyList<string> GetAvailableDriveLetters();
    string? GetMappedUnc(string driveLetter);
    WindowsCircleFilesStoredCredential? GetCredential(string target);
    WindowsCircleFilesStoredLabel? GetLabel(string uncPath);
    void ProbeEndpoint(string endpoint);
    void SaveCredential(string target, string accountName, string ownershipId, ReadOnlySpan<byte> secret);
    void MapDrive(string driveLetter, string uncPath, string accountName, ReadOnlySpan<byte> secret);
    string ReadShareFile(string uncPath, string fileName);
    void SaveLabel(string uncPath, string friendlyName, string ownershipId);
    void UnmapDrive(string driveLetter, string expectedUncPath);
    void DeleteLabel(string uncPath, string friendlyName, string ownershipId);
    void DeleteCredential(string target, string accountName, string ownershipId);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsCircleFilesMemberMapper : ICircleFilesMemberMapper
{
    private readonly IWindowsCircleFilesMappingOperations operations;

    public WindowsCircleFilesMemberMapper() : this(new WindowsCircleFilesMappingOperations())
    {
    }

    internal WindowsCircleFilesMemberMapper(IWindowsCircleFilesMappingOperations operations)
    {
        this.operations = operations;
    }

    public ValueTask<CircleFilesMemberMappingPlan> PreviewAsync(
        CircleFilesMemberMappingRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = CreatePlan(request, allowUnselectedDrive: true);
        if (plan.DriveLetter.Length == 0)
        {
            return ValueTask.FromResult(plan);
        }
        var drive = operations.GetMappedUnc(plan.DriveLetter);
        if (drive is not null && !Same(drive, plan.UncPath))
        {
            throw Collision("mapping_drive_collision", "The selected drive letter is already in use.");
        }

        using var credential = operations.GetCredential(plan.CredentialTarget);
        if (credential is not null
            && (credential.Target != plan.CredentialTarget
                || credential.AccountName != request.AccountName
                || credential.OwnershipId != plan.OwnershipId))
        {
            throw Collision(
                "mapping_credential_collision",
                "The exact Windows credential target is already in use.");
        }

        var label = operations.GetLabel(plan.UncPath);
        if (label is not null
            && (label.FriendlyName != plan.FriendlyName || label.OwnershipId != plan.OwnershipId))
        {
            throw Collision(
                "mapping_label_collision",
                "The exact Explorer share label is already in use.");
        }

        if (drive is null
            && !plan.AvailableDriveLetters.Contains(plan.DriveLetter, StringComparer.Ordinal))
        {
            throw Collision("mapping_drive_collision", "The selected drive letter is already in use.");
        }

        return ValueTask.FromResult(plan);
    }

    public ValueTask<CircleFilesMemberMappingInspection> InspectAsync(
        CircleFilesMemberMappingRequest request,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = CreatePlan(request);
        return ValueTask.FromResult(new CircleFilesMemberMappingInspection(
            InspectExact(request, plan),
            plan));
    }

    public async ValueTask<CircleFilesMemberMappingResult> MapAsync(
        CircleFilesMemberMappingRequest request,
        string expectedPlanId,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = CreatePlan(request);
        ValidatePlanId(expectedPlanId, plan.PlanId);
        var status = InspectExact(request, plan);
        if (status == "mapped")
        {
            ValidateShare(request, plan);
            return new CircleFilesMemberMappingResult("already-mapped", plan);
        }
        if (operations.GetMappedUnc(plan.DriveLetter) is null
            && !plan.AvailableDriveLetters.Contains(plan.DriveLetter, StringComparer.Ordinal))
        {
            throw Collision("mapping_drive_collision", "The selected drive letter is already in use.");
        }
        try
        {
            operations.ProbeEndpoint(plan.Endpoint);
        }
        catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException)
        {
            throw new CircleFilesHostingException(
                "mapping_endpoint_unreachable",
                "The exact private SMB endpoint could not be reached.");
        }

        var credentialCreated = false;
        var driveCreated = false;
        var labelCreated = false;
        try
        {
            using (var credential = operations.GetCredential(plan.CredentialTarget))
            {
                if (credential is null)
                {
                    operations.SaveCredential(
                        plan.CredentialTarget,
                        request.AccountName,
                        plan.OwnershipId,
                        secret.Span);
                    credentialCreated = true;
                }
            }

            if (operations.GetMappedUnc(plan.DriveLetter) is null)
            {
                operations.MapDrive(
                    plan.DriveLetter,
                    plan.UncPath,
                    request.AccountName,
                    secret.Span);
                driveCreated = true;
            }

            ValidateShare(request, plan);
            if (operations.GetLabel(plan.UncPath) is null)
            {
                operations.SaveLabel(plan.UncPath, plan.FriendlyName, plan.OwnershipId);
                labelCreated = true;
            }

            return new CircleFilesMemberMappingResult("mapped", CreatePlan(request));
        }
        catch
        {
            if (labelCreated)
            {
                TryRollback(() => operations.DeleteLabel(
                    plan.UncPath, plan.FriendlyName, plan.OwnershipId));
            }
            if (driveCreated)
            {
                TryRollback(() => operations.UnmapDrive(plan.DriveLetter, plan.UncPath));
            }
            if (credentialCreated)
            {
                TryRollback(() => operations.DeleteCredential(
                    plan.CredentialTarget,
                    request.AccountName,
                    plan.OwnershipId));
            }
            throw;
        }
    }

    public ValueTask<CircleFilesMemberMappingResult> UnmapAsync(
        CircleFilesMemberMappingRequest request,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = CreatePlan(request);
        var status = InspectExact(request, plan);
        if (status == "unmapped")
        {
            return ValueTask.FromResult(new CircleFilesMemberMappingResult("already-unmapped", plan));
        }

        if (operations.GetMappedUnc(plan.DriveLetter) is not null)
        {
            operations.UnmapDrive(plan.DriveLetter, plan.UncPath);
        }
        if (operations.GetLabel(plan.UncPath) is not null)
        {
            operations.DeleteLabel(plan.UncPath, plan.FriendlyName, plan.OwnershipId);
        }
        using (var credential = operations.GetCredential(plan.CredentialTarget))
        {
            if (credential is not null)
            {
                operations.DeleteCredential(
                    plan.CredentialTarget,
                    request.AccountName,
                    plan.OwnershipId);
            }
        }

        return ValueTask.FromResult(
            new CircleFilesMemberMappingResult("unmapped", CreatePlan(request)));
    }

    private CircleFilesMemberMappingPlan CreatePlan(
        CircleFilesMemberMappingRequest request,
        bool allowUnselectedDrive = false)
    {
        ValidateRequest(request, allowUnselectedDrive);
        var shareName = "balls-" + request.ProviderId.Replace("-", "", StringComparison.Ordinal)[..12];
        var uncPath = $@"\\{request.Endpoint}\{shareName}";
        var friendlyName = request.CircleName.Trim();
        var ownershipId = HashCanonical(
            request.CircleId, request.ContributionId, request.GrantId, request.MemberId,
            request.Endpoint, request.DriveLetter, uncPath, request.AccountName,
            request.GrantOwnershipId, request.Access, request.Generation.ToString());
        var planId = HashCanonical(
            CircleFilesMemberMappingContract.Version.ToString(), ownershipId,
            request.Endpoint, request.DriveLetter, friendlyName);
        return new CircleFilesMemberMappingPlan(
            CircleFilesMemberMappingContract.Version,
            planId,
            request.Endpoint,
            uncPath,
            request.Endpoint,
            request.DriveLetter,
            friendlyName,
            ownershipId,
            operations.GetAvailableDriveLetters(),
            [
                $"Save the exact {request.AccountName} grant credential for {request.Endpoint} in the current-user Windows Credential Manager.",
                $"Persistently map {uncPath} to {request.DriveLetter}: without elevation or replacement.",
                "Verify the exact Balls host and grant ownership markers through the mapped share.",
                $"Show the mapping in Explorer as {friendlyName}.",
            ]);
    }

    private string InspectExact(
        CircleFilesMemberMappingRequest request,
        CircleFilesMemberMappingPlan plan)
    {
        var owned = 0;
        var mapping = operations.GetMappedUnc(plan.DriveLetter);
        if (mapping is not null)
        {
            if (!Same(mapping, plan.UncPath)) throw ResourceCollision();
            owned++;
        }

        using (var credential = operations.GetCredential(plan.CredentialTarget))
        {
            if (credential is not null)
            {
                if (credential.Target != plan.CredentialTarget
                    || credential.AccountName != request.AccountName
                    || credential.OwnershipId != plan.OwnershipId)
                {
                    throw ResourceCollision();
                }
                owned++;
            }
        }

        var label = operations.GetLabel(plan.UncPath);
        if (label is not null)
        {
            if (label.FriendlyName != plan.FriendlyName || label.OwnershipId != plan.OwnershipId)
            {
                throw ResourceCollision();
            }
            owned++;
        }

        return owned switch
        {
            0 => "unmapped",
            3 => "mapped",
            _ => "partial",
        };
    }

    private void ValidateShare(
        CircleFilesMemberMappingRequest request,
        CircleFilesMemberMappingPlan plan)
    {
        try
        {
            using var host = JsonDocument.Parse(
                operations.ReadShareFile(plan.UncPath, ".balls-owned-v1.json"));
            using var grant = JsonDocument.Parse(
                operations.ReadShareFile(
                    plan.UncPath,
                    $".balls-grant-{request.GrantId}-g{request.Generation}-v1.json"));
            var hostRoot = host.RootElement;
            var grantRoot = grant.RootElement;
            if (!JsonEquals(hostRoot, "circleId", request.CircleId)
                || !JsonEquals(hostRoot, "contributionId", request.ContributionId)
                || !JsonEquals(hostRoot, "providerId", request.ProviderId)
                || !JsonNumberEquals(hostRoot, "contractVersion", 1)
                || !JsonEquals(grantRoot, "OwnershipId", request.GrantOwnershipId)
                || !JsonEquals(grantRoot, "CircleId", request.CircleId)
                || !JsonEquals(grantRoot, "ContributionId", request.ContributionId)
                || !JsonEquals(grantRoot, "GrantId", request.GrantId)
                || !JsonEquals(grantRoot, "MemberId", request.MemberId)
                || !JsonEquals(grantRoot, "AccountName", request.AccountName)
                || !JsonEquals(grantRoot, "Access", request.Access)
                || !JsonNumberEquals(grantRoot, "Generation", request.Generation)
                || !JsonNumberEquals(grantRoot, "ContractVersion", 1))
            {
                throw ShareMismatch();
            }
        }
        catch (CircleFilesHostingException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw ShareMismatch();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CircleFilesHostingException(
                "mapping_endpoint_unreachable",
                "The exact private SMB endpoint could not be reached.");
        }
    }

    private static void ValidateRequest(
        CircleFilesMemberMappingRequest request,
        bool allowUnselectedDrive)
    {
        if (!Guid.TryParseExact(request.CircleId, "D", out _)
            || !Guid.TryParseExact(request.ContributionId, "D", out _)
            || !Guid.TryParseExact(request.ProviderId, "D", out _)
            || !Guid.TryParseExact(request.GrantId, "D", out _)
            || !Guid.TryParseExact(request.MemberId, "D", out _)
            || request.AccountName.Length is < 1 or > 20
            || request.GrantOwnershipId.Length != 64
            || request.Access is not ("read-only" or "read-write")
            || request.Generation <= 0
            || !(allowUnselectedDrive && request.DriveLetter.Length == 0)
                && (request.DriveLetter.Length != 1 || request.DriveLetter[0] is < 'D' or > 'Z')
            || request.CircleName.Trim().Length is < 1 or > 80
            || request.CircleName.Any(character => char.IsControl(character) || character is '\\' or '/'))
        {
            throw new CircleFilesHostingException(
                "mapping_request_invalid",
                "The Circle Files Explorer mapping request is invalid.");
        }

        if (!IPAddress.TryParse(request.Endpoint, out var endpoint)
            || endpoint.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || !IsPrivateOrLoopback(endpoint)
            || endpoint.ToString() != request.Endpoint)
        {
            throw new CircleFilesHostingException(
                "mapping_endpoint_invalid",
                "Mapping requires a canonical numeric private or loopback IPv4 endpoint.");
        }
    }

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 127
            || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }

    private static bool JsonEquals(JsonElement root, string name, string expected) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() == expected;

    private static bool JsonNumberEquals(JsonElement root, string name, long expected) =>
        root.TryGetProperty(name, out var value)
        && value.TryGetInt64(out var actual)
        && actual == expected;

    private static void ValidatePlanId(string expected, string actual)
    {
        if (expected is null
            || expected.Length != actual.Length
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual)))
        {
            throw new CircleFilesHostingException(
                "mapping_plan_changed",
                "The Explorer mapping plan changed; preview it again before approval.");
        }
    }

    private static string HashCanonical(params string[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length)));
            hash.AppendData(bytes);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static bool Same(string left, string right) =>
        string.Equals(left.TrimEnd('\\'), right.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static void TryRollback(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static CircleFilesHostingException Collision(string code, string message) => new(code, message);
    private static CircleFilesHostingException ResourceCollision() => Collision(
        "mapping_resource_collision",
        "A drive, credential, or Explorer label no longer matches the exact Balls-owned mapping.");
    private static CircleFilesHostingException ShareMismatch() => Collision(
        "mapping_share_identity_mismatch",
        "The SMB share did not present the exact authorized Balls ownership markers.");
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsCircleFilesMappingOperations : IWindowsCircleFilesMappingOperations
{
    private const int ErrorNotConnected = 2250;
    private const int ErrorConnectionUnavailable = 1201;
    private const int ErrorNotFound = 1168;
    private const int ResourceTypeDisk = 1;
    private const int ConnectUpdateProfile = 1;
    private const int CredentialTypeDomainPassword = 2;
    private const int CredentialPersistLocalMachine = 2;
    private const string LabelRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2";

    public IReadOnlyList<string> GetAvailableDriveLetters()
    {
        var physical = DriveInfo.GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]).ToString())
            .ToHashSet(StringComparer.Ordinal);
        return Enumerable.Range('D', 'Z' - 'D' + 1)
            .Select(value => ((char)value).ToString())
            .Where(letter => !physical.Contains(letter) && GetMappedUnc(letter) is null)
            .ToArray();
    }

    public void ProbeEndpoint(string endpoint)
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            socket.ConnectAsync(IPAddress.Parse(endpoint), 445, timeout.Token)
                .AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException exception)
        {
            throw new IOException("The private SMB endpoint did not answer in time.", exception);
        }
    }

    public string? GetMappedUnc(string driveLetter)
    {
        var capacity = 1024;
        var value = new StringBuilder(capacity);
        var result = WNetGetConnection($"{driveLetter}:", value, ref capacity);
        if (result == 0) return value.ToString();
        if (result is not (ErrorNotConnected or ErrorConnectionUnavailable)) ThrowNative(result);
        using var key = Registry.CurrentUser.OpenSubKey($@"Network\{driveLetter}");
        return key?.GetValue("RemotePath") as string;
    }

    public WindowsCircleFilesStoredCredential? GetCredential(string target)
    {
        if (!CredRead(target, CredentialTypeDomainPassword, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == ErrorNotFound) return null;
            throw NativeFailure();
        }

        try
        {
            var native = Marshal.PtrToStructure<NativeCredential>(pointer);
            var unicode = new byte[native.CredentialBlobSize];
            byte[]? utf8 = null;
            try
            {
                if (unicode.Length > 0) Marshal.Copy(native.CredentialBlob, unicode, 0, unicode.Length);
                var chars = Encoding.Unicode.GetChars(unicode);
                try { utf8 = Encoding.UTF8.GetBytes(chars); }
                finally { Array.Clear(chars); }
                return new WindowsCircleFilesStoredCredential(
                    native.TargetName ?? string.Empty,
                    native.UserName ?? string.Empty,
                    native.Comment ?? string.Empty,
                    utf8);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(unicode);
                if (utf8 is null) { }
            }
        }
        finally { CredFree(pointer); }
    }

    public void SaveCredential(
        string target,
        string accountName,
        string ownershipId,
        ReadOnlySpan<byte> secret)
    {
        var unicode = ToUnicode(secret);
        var blob = Marshal.AllocHGlobal(unicode.Length);
        try
        {
            Marshal.Copy(unicode, 0, blob, unicode.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeDomainPassword,
                TargetName = target,
                Comment = ownershipId,
                CredentialBlobSize = (uint)unicode.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = accountName,
            };
            if (!CredWrite(ref credential, 0)) throw NativeFailure();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(unicode);
            ZeroAndFree(blob, unicode.Length);
        }
    }

    public unsafe void MapDrive(
        string driveLetter,
        string uncPath,
        string accountName,
        ReadOnlySpan<byte> secret)
    {
        var encoded = secret.ToArray();
        var chars = Encoding.UTF8.GetChars(encoded);
        try
        {
            var resource = new NativeNetResource
            {
                ResourceType = ResourceTypeDisk,
                LocalName = $"{driveLetter}:",
                RemoteName = uncPath,
            };
            fixed (char* password = chars)
            {
                var result = WNetAddConnection2(ref resource, (IntPtr)password, accountName, ConnectUpdateProfile);
                if (result != 0) ThrowNative(result);
            }
        }
        finally
        {
            Array.Clear(chars);
            CryptographicOperations.ZeroMemory(encoded);
        }
    }

    public string ReadShareFile(string uncPath, string fileName) =>
        File.ReadAllText(Path.Combine(uncPath, fileName), Encoding.UTF8);

    public WindowsCircleFilesStoredLabel? GetLabel(string uncPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(LabelKey(uncPath));
        var name = key?.GetValue("_LabelFromReg") as string;
        var ownership = key?.GetValue("BallsOwnershipId") as string;
        return name is null && ownership is null
            ? null
            : new WindowsCircleFilesStoredLabel(name ?? string.Empty, ownership ?? string.Empty);
    }

    public void SaveLabel(string uncPath, string friendlyName, string ownershipId)
    {
        using var key = Registry.CurrentUser.CreateSubKey(LabelKey(uncPath), writable: true)
            ?? throw new IOException("Windows did not create the Explorer label key.");
        if (key.GetValue("_LabelFromReg") is not null || key.GetValue("BallsOwnershipId") is not null)
        {
            throw new CircleFilesHostingException(
                "mapping_label_collision",
                "The exact Explorer share label is already in use.");
        }
        key.SetValue("_LabelFromReg", friendlyName, RegistryValueKind.String);
        key.SetValue("BallsOwnershipId", ownershipId, RegistryValueKind.String);
    }

    public void UnmapDrive(string driveLetter, string expectedUncPath)
    {
        var actual = GetMappedUnc(driveLetter);
        if (actual is null) return;
        if (!string.Equals(actual.TrimEnd('\\'), expectedUncPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            throw new CircleFilesHostingException(
                "mapping_resource_collision",
                "The selected drive letter now belongs to another connection and was preserved.");
        }
        var result = WNetCancelConnection2($"{driveLetter}:", ConnectUpdateProfile, false);
        if (result != 0) ThrowNative(result);
    }

    public void DeleteLabel(string uncPath, string friendlyName, string ownershipId)
    {
        var path = LabelKey(uncPath);
        using (var key = Registry.CurrentUser.OpenSubKey(path, writable: true))
        {
            if (key is null) return;
            if (key.GetValue("_LabelFromReg") as string != friendlyName
                || key.GetValue("BallsOwnershipId") as string != ownershipId)
            {
                throw new CircleFilesHostingException(
                    "mapping_resource_collision",
                    "The Explorer label is not the exact Balls-owned value and was preserved.");
            }
            key.DeleteValue("_LabelFromReg", throwOnMissingValue: true);
            key.DeleteValue("BallsOwnershipId", throwOnMissingValue: true);
        }
        try { Registry.CurrentUser.DeleteSubKey(path, throwOnMissingSubKey: false); }
        catch (InvalidOperationException) { }
    }

    public void DeleteCredential(
        string target,
        string accountName,
        string ownershipId)
    {
        using var actual = GetCredential(target);
        if (actual is null) return;
        if (actual.Target != target
            || actual.AccountName != accountName
            || actual.OwnershipId != ownershipId)
        {
            throw new CircleFilesHostingException(
                "mapping_resource_collision",
                "The Windows credential is not the exact Balls-owned value and was preserved.");
        }
        if (!CredDelete(target, CredentialTypeDomainPassword, 0)) throw NativeFailure();
    }

    private static byte[] ToUnicode(ReadOnlySpan<byte> secret)
    {
        var encoded = secret.ToArray();
        var chars = Encoding.UTF8.GetChars(encoded);
        try { return Encoding.Unicode.GetBytes(chars); }
        finally
        {
            Array.Clear(chars);
            CryptographicOperations.ZeroMemory(encoded);
        }
    }

    private static string LabelKey(string uncPath) =>
        LabelRoot + "\\" + uncPath.Replace('\\', '#');

    private static void ZeroAndFree(IntPtr pointer, int length)
    {
        for (var index = 0; index < length; index++) Marshal.WriteByte(pointer, index, 0);
        Marshal.FreeHGlobal(pointer);
    }

    private static Exception NativeFailure() => new IOException(
        "Windows rejected the exact Circle Files mapping operation.",
        new Win32Exception(Marshal.GetLastWin32Error()));

    private static void ThrowNative(int error) => throw new IOException(
        "Windows rejected the exact Circle Files mapping operation.",
        new Win32Exception(error));

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(
        ref NativeNetResource netResource,
        IntPtr password,
        string userName,
        int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeNetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;

        public int ResourceType { set => Type = value; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
