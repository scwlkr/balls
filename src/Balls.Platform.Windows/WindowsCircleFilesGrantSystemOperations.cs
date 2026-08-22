using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Balls.Platform;

namespace Balls.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsCircleFilesGrantSystemOperations : IWindowsCircleFilesGrantOperations
{
    private readonly WindowsCircleFilesGrantPowerShell powerShell = new();

    public async ValueTask<WindowsCircleFilesOwnedState> InspectAsync(
        WindowsCircleFilesGrantHelperPlan plan,
        WindowsCircleFilesGrantOperationStep step,
        CancellationToken cancellationToken) => step switch
        {
            WindowsCircleFilesGrantOperationStep.LocalAccount =>
                await powerShell.InspectAccountAsync(plan, cancellationToken).ConfigureAwait(false),
            WindowsCircleFilesGrantOperationStep.GrantMarker => InspectMarker(plan),
            WindowsCircleFilesGrantOperationStep.FolderAcl => InspectFolderAcl(plan),
            WindowsCircleFilesGrantOperationStep.ShareAccess =>
                await powerShell.InspectShareAccessAsync(plan, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(step)),
        };

    public async ValueTask ApplyAsync(
        WindowsCircleFilesGrantHelperPlan plan,
        WindowsCircleFilesGrantOperationStep step,
        CancellationToken cancellationToken)
    {
        switch (step)
        {
            case WindowsCircleFilesGrantOperationStep.LocalAccount:
                await powerShell.CreateAccountAsync(plan, cancellationToken).ConfigureAwait(false);
                break;
            case WindowsCircleFilesGrantOperationStep.GrantMarker:
                ApplyMarker(plan);
                break;
            case WindowsCircleFilesGrantOperationStep.FolderAcl:
                ApplyFolderAcl(plan);
                break;
            case WindowsCircleFilesGrantOperationStep.ShareAccess:
                await powerShell.GrantShareAccessAsync(plan, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step));
        }
    }

    public async ValueTask RollbackAsync(
        WindowsCircleFilesGrantHelperPlan plan,
        WindowsCircleFilesGrantOperationStep step,
        CancellationToken cancellationToken)
    {
        switch (step)
        {
            case WindowsCircleFilesGrantOperationStep.ShareAccess:
                await powerShell.RevokeShareAccessAsync(plan, cancellationToken).ConfigureAwait(false);
                break;
            case WindowsCircleFilesGrantOperationStep.FolderAcl:
                RollbackFolderAcl(plan);
                break;
            case WindowsCircleFilesGrantOperationStep.GrantMarker:
                RemoveMarker(plan);
                break;
            case WindowsCircleFilesGrantOperationStep.LocalAccount:
                await powerShell.RemoveAccountAsync(plan, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step));
        }
    }

    private static string MarkerPath(WindowsCircleFilesGrantHelperPlan plan) =>
        Path.Combine(
            plan.PublicPlan.FolderPath,
            $".balls-grant-{plan.Request.GrantId}-g{plan.Request.Generation}-v1.json");

    private static WindowsCircleFilesOwnedState InspectMarker(WindowsCircleFilesGrantHelperPlan plan)
    {
        var path = MarkerPath(plan);
        if (!File.Exists(path))
        {
            return WindowsCircleFilesOwnedState.Missing;
        }
        try
        {
            var marker = JsonSerializer.Deserialize<WindowsCircleFilesGrantMarker>(File.ReadAllText(path));
            return marker is not null
                && marker.ContractVersion == CircleFilesGrantCredentialContract.Version
                && marker.OwnershipId == plan.PublicPlan.OwnershipId
                && marker.PlanId == plan.PublicPlan.PlanId
                && marker.CircleId == plan.Request.Host.CircleId
                && marker.ContributionId == plan.Request.Host.ContributionId
                && marker.GrantId == plan.Request.GrantId
                && marker.MemberId == plan.Request.MemberId
                && marker.Access == plan.Request.Access
                && marker.Generation == plan.Request.Generation
                && marker.AccountName == plan.PublicPlan.AccountName
                && marker.FolderPath.Equals(plan.PublicPlan.FolderPath, StringComparison.OrdinalIgnoreCase)
                && marker.PreMutationSddl.Length is > 0 and <= 8192
                && HasProtectedOwnerSystemFileAcl(path, plan.OwnerSid)
                    ? WindowsCircleFilesOwnedState.Owned
                    : WindowsCircleFilesOwnedState.Collision;
        }
        catch (Exception exception) when (exception is JsonException or IOException or ArgumentException)
        {
            return WindowsCircleFilesOwnedState.Collision;
        }
    }

    private static void ApplyMarker(WindowsCircleFilesGrantHelperPlan plan)
    {
        if (InspectMarker(plan) != WindowsCircleFilesOwnedState.Missing)
        {
            throw new InvalidOperationException("The grant marker already exists.");
        }
        var marker = new WindowsCircleFilesGrantMarker(
            CircleFilesGrantCredentialContract.Version,
            plan.PublicPlan.OwnershipId,
            plan.PublicPlan.PlanId,
            plan.Request.Host.CircleId,
            plan.Request.Host.ContributionId,
            plan.Request.GrantId,
            plan.Request.MemberId,
            plan.Request.Access,
            plan.Request.Generation,
            plan.PublicPlan.AccountName,
            plan.PublicPlan.FolderPath,
            GetDirectorySddl(plan.PublicPlan.FolderPath));
        var path = MarkerPath(plan);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, marker);
            stream.WriteByte((byte)'\n');
            stream.Flush(flushToDisk: true);
        }
        ApplyOwnerSystemFileAcl(path, plan.OwnerSid);
    }

    private static void RemoveMarker(WindowsCircleFilesGrantHelperPlan plan)
    {
        if (InspectMarker(plan) != WindowsCircleFilesOwnedState.Owned)
        {
            throw Collision("The grant marker changed and was left untouched.");
        }
        File.Delete(MarkerPath(plan));
    }

    private static WindowsCircleFilesOwnedState InspectFolderAcl(WindowsCircleFilesGrantHelperPlan plan)
    {
        if (InspectMarker(plan) != WindowsCircleFilesOwnedState.Owned)
        {
            return WindowsCircleFilesOwnedState.Missing;
        }
        var marker = ReadMarker(plan);
        var current = GetDirectorySddl(plan.PublicPlan.FolderPath);
        if (string.Equals(current, GetDesiredDirectorySddl(marker, plan), StringComparison.Ordinal))
        {
            return WindowsCircleFilesOwnedState.Owned;
        }
        return string.Equals(current, marker.PreMutationSddl, StringComparison.Ordinal)
            ? WindowsCircleFilesOwnedState.Missing
            : WindowsCircleFilesOwnedState.Collision;
    }

    private static void ApplyFolderAcl(WindowsCircleFilesGrantHelperPlan plan)
    {
        var marker = ReadMarker(plan);
        if (!string.Equals(
                GetDirectorySddl(plan.PublicPlan.FolderPath),
                marker.PreMutationSddl,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The contributed folder ACL changed.");
        }
        var security = CreateDesiredDirectorySecurity(marker, plan);
        new DirectoryInfo(plan.PublicPlan.FolderPath).SetAccessControl(security);
    }

    private static void RollbackFolderAcl(WindowsCircleFilesGrantHelperPlan plan)
    {
        var marker = ReadMarker(plan);
        if (InspectFolderAcl(plan) != WindowsCircleFilesOwnedState.Owned)
        {
            throw Collision("The contributed folder ACL changed and was left untouched.");
        }
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm(marker.PreMutationSddl);
        new DirectoryInfo(plan.PublicPlan.FolderPath).SetAccessControl(security);
    }

    private static WindowsCircleFilesGrantMarker ReadMarker(WindowsCircleFilesGrantHelperPlan plan) =>
        JsonSerializer.Deserialize<WindowsCircleFilesGrantMarker>(File.ReadAllText(MarkerPath(plan)))
        ?? throw new InvalidDataException("The grant marker is invalid.");

    private static DirectorySecurity CreateDesiredDirectorySecurity(
        WindowsCircleFilesGrantMarker marker,
        WindowsCircleFilesGrantHelperPlan plan)
    {
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm(marker.PreMutationSddl);
        var account = new NTAccount(Environment.MachineName, plan.PublicPlan.AccountName);
        var rights = plan.Request.Access == "read-only"
            ? FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize
            : FileSystemRights.Modify | FileSystemRights.Synchronize;
        security.AddAccessRule(new FileSystemAccessRule(
            account,
            rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    private static string GetDesiredDirectorySddl(
        WindowsCircleFilesGrantMarker marker,
        WindowsCircleFilesGrantHelperPlan plan) =>
        CreateDesiredDirectorySecurity(marker, plan)
            .GetSecurityDescriptorSddlForm(AccessControlSections.All);

    private static string GetDirectorySddl(string path) =>
        new DirectoryInfo(path).GetAccessControl(AccessControlSections.All)
            .GetSecurityDescriptorSddlForm(AccessControlSections.All);

    private static void ApplyOwnerSystemFileAcl(string path, string ownerSid)
    {
        var owner = new SecurityIdentifier(ownerSid);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    private static bool HasProtectedOwnerSystemFileAcl(string path, string ownerSid)
    {
        var security = new FileInfo(path).GetAccessControl(AccessControlSections.All);
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToArray();
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            ownerSid,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
        };
        return security.AreAccessRulesProtected
            && security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier actualOwner
            && actualOwner.Value == ownerSid
            && rules.Length == 2
            && rules.All(rule => rule.AccessControlType == AccessControlType.Allow
                && rule.FileSystemRights == FileSystemRights.FullControl
                && rule.IdentityReference is SecurityIdentifier sid
                && expected.Remove(sid.Value))
            && expected.Count == 0;
    }

    private static CircleFilesHostingException Collision(string message) =>
        new("grant_resource_collision", message);

    private sealed record WindowsCircleFilesGrantMarker(
        int ContractVersion,
        string OwnershipId,
        string PlanId,
        string CircleId,
        string ContributionId,
        string GrantId,
        string MemberId,
        string Access,
        long Generation,
        string AccountName,
        string FolderPath,
        string PreMutationSddl);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsCircleFilesGrantPowerShell
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private const int MaximumOutputCharacters = 16 * 1024;

    internal ValueTask<WindowsCircleFilesOwnedState> InspectAccountAsync(
        WindowsCircleFilesGrantHelperPlan plan,
        CancellationToken cancellationToken) => InvokeStateAsync("InspectAccount", plan, cancellationToken);

    internal ValueTask<WindowsCircleFilesOwnedState> InspectShareAccessAsync(
        WindowsCircleFilesGrantHelperPlan plan,
        CancellationToken cancellationToken) => InvokeStateAsync("InspectShareAccess", plan, cancellationToken);

    internal async ValueTask CreateAccountAsync(WindowsCircleFilesGrantHelperPlan plan, CancellationToken token) =>
        _ = await InvokeAsync("CreateAccount", plan, token).ConfigureAwait(false);

    internal async ValueTask RemoveAccountAsync(WindowsCircleFilesGrantHelperPlan plan, CancellationToken token) =>
        _ = await InvokeAsync("RemoveAccount", plan, token).ConfigureAwait(false);

    internal async ValueTask GrantShareAccessAsync(WindowsCircleFilesGrantHelperPlan plan, CancellationToken token) =>
        _ = await InvokeAsync("GrantShareAccess", plan, token).ConfigureAwait(false);

    internal async ValueTask RevokeShareAccessAsync(WindowsCircleFilesGrantHelperPlan plan, CancellationToken token) =>
        _ = await InvokeAsync("RevokeShareAccess", plan, token).ConfigureAwait(false);

    private async ValueTask<WindowsCircleFilesOwnedState> InvokeStateAsync(
        string command,
        WindowsCircleFilesGrantHelperPlan plan,
        CancellationToken token)
    {
        using var document = JsonDocument.Parse(await InvokeAsync(command, plan, token).ConfigureAwait(false));
        return document.RootElement.GetProperty("State").GetString() switch
        {
            "Missing" => WindowsCircleFilesOwnedState.Missing,
            "Owned" => WindowsCircleFilesOwnedState.Owned,
            "Collision" => WindowsCircleFilesOwnedState.Collision,
            _ => throw new InvalidDataException("The grant helper returned an invalid state."),
        };
    }

    private static async Task<string> InvokeAsync(
        string command,
        WindowsCircleFilesGrantHelperPlan plan,
        CancellationToken token)
    {
        var executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.Environment.Remove("PSModulePath");
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)));
        var input = JsonSerializer.Serialize(new
        {
            Command = command,
            AccountName = plan.PublicPlan.AccountName,
            Password = Encoding.UTF8.GetString(plan.Secret),
            Description = $"Balls grant v1 {plan.PublicPlan.OwnershipId}",
            ShareName = plan.PublicPlan.ShareName,
            AccessRight = plan.Request.Access == "read-only" ? "Read" : "Change",
        });
        try
        {
            return await BoundedWindowsInspectionProcessRunner.RunWithInputAsync(
                startInfo, input, Timeout, MaximumOutputCharacters, token).ConfigureAwait(false);
        }
        catch (WindowsInspectionException exception)
        {
            throw new IOException("The fixed Windows grant command failed.", exception);
        }
    }

    internal const string Script =
        """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        $request = [Console]::In.ReadToEnd() | Microsoft.PowerShell.Utility\ConvertFrom-Json
        Add-Type -TypeDefinition @'
        using System;
        using System.Collections.Generic;
        using System.ComponentModel;
        using System.Runtime.InteropServices;
        public static class BallsGrantRights {
          [StructLayout(LayoutKind.Sequential)] struct LSA_OBJECT_ATTRIBUTES { public int Length; public IntPtr RootDirectory; public IntPtr ObjectName; public uint Attributes; public IntPtr SecurityDescriptor; public IntPtr SecurityQualityOfService; }
          [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct LSA_UNICODE_STRING { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }
          [DllImport("advapi32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool LogonUser(string user, string domain, string password, int logonType, int provider, out IntPtr token);
          [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
          [DllImport("advapi32.dll")] static extern uint LsaOpenPolicy(IntPtr system, ref LSA_OBJECT_ATTRIBUTES attributes, uint access, out IntPtr policy);
          [DllImport("advapi32.dll")] static extern uint LsaAddAccountRights(IntPtr policy, byte[] sid, LSA_UNICODE_STRING[] rights, uint count);
          [DllImport("advapi32.dll")] static extern uint LsaEnumerateAccountRights(IntPtr policy, byte[] sid, out IntPtr rights, out uint count);
          [DllImport("advapi32.dll")] static extern uint LsaClose(IntPtr handle);
          [DllImport("advapi32.dll")] static extern uint LsaFreeMemory(IntPtr buffer);
          static readonly string[] Expected = { "SeDenyInteractiveLogonRight", "SeDenyRemoteInteractiveLogonRight", "SeDenyBatchLogonRight", "SeDenyServiceLogonRight" };
          static LSA_UNICODE_STRING Make(string value) { var p=Marshal.StringToHGlobalUni(value); return new LSA_UNICODE_STRING { Buffer=p, Length=(ushort)(value.Length*2), MaximumLength=(ushort)((value.Length+1)*2) }; }
          static IntPtr Open() { var a=new LSA_OBJECT_ATTRIBUTES { Length=Marshal.SizeOf(typeof(LSA_OBJECT_ATTRIBUTES)) }; IntPtr p; uint s=LsaOpenPolicy(IntPtr.Zero, ref a, 0x810, out p); if(s!=0) throw new Win32Exception((int)s); return p; }
          public static bool PasswordWorks(string user,string password) { IntPtr t; if(!LogonUser(user,".",password,3,0,out t)) return false; CloseHandle(t); return true; }
          public static void Add(string sidText) { var sid=new System.Security.Principal.SecurityIdentifier(sidText); var b=new byte[sid.BinaryLength]; sid.GetBinaryForm(b,0); IntPtr p=Open(); var values=new LSA_UNICODE_STRING[Expected.Length]; try { for(int i=0;i<values.Length;i++) values[i]=Make(Expected[i]); uint s=LsaAddAccountRights(p,b,values,(uint)values.Length); if(s!=0) throw new Win32Exception((int)s); } finally { foreach(var v in values) if(v.Buffer!=IntPtr.Zero) Marshal.FreeHGlobal(v.Buffer); LsaClose(p); } }
          public static bool Exact(string sidText) { var sid=new System.Security.Principal.SecurityIdentifier(sidText); var b=new byte[sid.BinaryLength]; sid.GetBinaryForm(b,0); IntPtr p=Open(), values=IntPtr.Zero; try { uint count; uint s=LsaEnumerateAccountRights(p,b,out values,out count); if(s!=0) return false; var found=new HashSet<string>(StringComparer.Ordinal); int size=Marshal.SizeOf(typeof(LSA_UNICODE_STRING)); for(int i=0;i<count;i++){ var v=(LSA_UNICODE_STRING)Marshal.PtrToStructure(IntPtr.Add(values,i*size),typeof(LSA_UNICODE_STRING)); found.Add(Marshal.PtrToStringUni(v.Buffer,v.Length/2)); } return found.SetEquals(Expected); } finally { if(values!=IntPtr.Zero)LsaFreeMemory(values); LsaClose(p); } }
        }
        '@
        function Get-AccountState {
          $user = Microsoft.PowerShell.LocalAccounts\Get-LocalUser -Name ([string]$request.AccountName) -ErrorAction SilentlyContinue
          if ($null -eq $user) { return 'Missing' }
          $groups = @(Microsoft.PowerShell.LocalAccounts\Get-LocalGroup | Where-Object { @((Microsoft.PowerShell.LocalAccounts\Get-LocalGroupMember -Name $_.Name -ErrorAction SilentlyContinue).SID.Value) -contains [string]$user.SID.Value })
          $owned = [string]$user.Description -eq [string]$request.Description -and [bool]$user.Enabled -and [bool]$user.PasswordNeverExpires -and -not [bool]$user.UserMayChangePassword -and $groups.Count -eq 0 -and [BallsGrantRights]::PasswordWorks([string]$request.AccountName,[string]$request.Password) -and [BallsGrantRights]::Exact([string]$user.SID.Value)
          if ($owned) { return 'Owned' }
          return 'Collision'
        }
        function Get-ShareState {
          $share = SmbShare\Get-SmbShare -Name ([string]$request.ShareName) -ErrorAction SilentlyContinue
          if ($null -eq $share -or -not [bool]$share.EncryptData) { return 'Collision' }
          $user = Microsoft.PowerShell.LocalAccounts\Get-LocalUser -Name ([string]$request.AccountName) -ErrorAction SilentlyContinue
          if ($null -eq $user) { return 'Missing' }
          $foreign = @(SmbShare\Get-SmbShare | Where-Object { [string]$_.Name -ne [string]$request.ShareName } | ForEach-Object { SmbShare\Get-SmbShareAccess -Name $_.Name -ErrorAction SilentlyContinue } | Where-Object { try { ([System.Security.Principal.NTAccount]$_.AccountName).Translate([System.Security.Principal.SecurityIdentifier]).Value -eq [string]$user.SID.Value } catch { $false } })
          if ($foreign.Count -ne 0) { return 'Collision' }
          $matches = @(SmbShare\Get-SmbShareAccess -Name ([string]$request.ShareName) -ErrorAction Stop | Where-Object { try { ([System.Security.Principal.NTAccount]$_.AccountName).Translate([System.Security.Principal.SecurityIdentifier]).Value -eq [string]$user.SID.Value } catch { $false } })
          if ($matches.Count -eq 0) { return 'Missing' }
          if ($matches.Count -eq 1 -and [string]$matches[0].AccessControlType -eq 'Allow' -and [string]$matches[0].AccessRight -eq [string]$request.AccessRight) { return 'Owned' }
          return 'Collision'
        }
        switch ([string]$request.Command) {
          'InspectAccount' { $state = Get-AccountState }
          'CreateAccount' {
            if ((Get-AccountState) -ne 'Missing') { throw 'account collision' }
            $secure = Microsoft.PowerShell.Security\ConvertTo-SecureString ([string]$request.Password) -AsPlainText -Force
            try {
              $user = Microsoft.PowerShell.LocalAccounts\New-LocalUser -Name ([string]$request.AccountName) -Password $secure -Description ([string]$request.Description) -AccountNeverExpires -PasswordNeverExpires -UserMayNotChangePassword -ErrorAction Stop
              Microsoft.PowerShell.LocalAccounts\Set-LocalUser -Name ([string]$request.AccountName) -PasswordNeverExpires $true -UserMayChangePassword $false -ErrorAction Stop
              $sam = [ADSI]('WinNT://./' + [string]$request.AccountName + ',user')
              $sam.Put('UserFlags', ([int]$sam.UserFlags.Value -bor 0x10000 -bor 0x40))
              $sam.SetInfo()
              Microsoft.PowerShell.LocalAccounts\Get-LocalGroup | Where-Object { @((Microsoft.PowerShell.LocalAccounts\Get-LocalGroupMember -Name $_.Name -ErrorAction SilentlyContinue).SID.Value) -contains [string]$user.SID.Value } | ForEach-Object { Microsoft.PowerShell.LocalAccounts\Remove-LocalGroupMember -Name $_.Name -Member $user.Name -ErrorAction Stop }
              [BallsGrantRights]::Add([string]$user.SID.Value)
              $state = Get-AccountState
            } catch {
              $candidate = Microsoft.PowerShell.LocalAccounts\Get-LocalUser -Name ([string]$request.AccountName) -ErrorAction SilentlyContinue
              if ($null -ne $candidate -and [string]$candidate.Description -eq [string]$request.Description -and [BallsGrantRights]::PasswordWorks([string]$request.AccountName,[string]$request.Password)) { Microsoft.PowerShell.LocalAccounts\Remove-LocalUser -Name ([string]$request.AccountName) -ErrorAction SilentlyContinue }
              throw
            }
          }
          'RemoveAccount' { if ((Get-AccountState) -ne 'Owned') { throw 'account ownership changed' }; Microsoft.PowerShell.LocalAccounts\Remove-LocalUser -Name ([string]$request.AccountName) -ErrorAction Stop; $state='Missing' }
          'InspectShareAccess' { $state = Get-ShareState }
          'GrantShareAccess' { if ((Get-ShareState) -ne 'Missing') { throw 'share access collision' }; $user=Microsoft.PowerShell.LocalAccounts\Get-LocalUser -Name ([string]$request.AccountName) -ErrorAction Stop; $account=$user.SID.Translate([System.Security.Principal.NTAccount]).Value; SmbShare\Grant-SmbShareAccess -Name ([string]$request.ShareName) -AccountName $account -AccessRight ([string]$request.AccessRight) -Force -ErrorAction Stop | Out-Null; $state=Get-ShareState }
          'RevokeShareAccess' { if ((Get-ShareState) -ne 'Owned') { throw 'share access ownership changed' }; $user=Microsoft.PowerShell.LocalAccounts\Get-LocalUser -Name ([string]$request.AccountName) -ErrorAction Stop; $account=$user.SID.Translate([System.Security.Principal.NTAccount]).Value; SmbShare\Revoke-SmbShareAccess -Name ([string]$request.ShareName) -AccountName $account -Force -ErrorAction Stop | Out-Null; $state=Get-ShareState }
          default { throw 'unsupported command' }
        }
        [PSCustomObject]@{ State=$state } | Microsoft.PowerShell.Utility\ConvertTo-Json -Compress
        """;
}
