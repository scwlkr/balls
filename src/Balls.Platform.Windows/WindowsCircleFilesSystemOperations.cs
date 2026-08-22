using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Balls.Platform;

namespace Balls.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsCircleFilesSystemOperations : IWindowsCircleFilesOperations
{
    internal const string JournalFileName = ".balls-operation-v1.json";
    private readonly WindowsCircleFilesPowerShell powerShell = new();

    public async ValueTask<WindowsCircleFilesOwnedState> InspectAsync(
        WindowsCircleFilesHelperPlan plan,
        WindowsCircleFilesOperationStep step,
        CancellationToken cancellationToken) =>
        step switch
        {
            WindowsCircleFilesOperationStep.FolderAcl => InspectFolderAcl(plan),
            WindowsCircleFilesOperationStep.OwnershipMarker => InspectMarker(plan),
            WindowsCircleFilesOperationStep.EncryptedShare => await powerShell
                .InspectShareAsync(plan, cancellationToken).ConfigureAwait(false),
            WindowsCircleFilesOperationStep.PrivateFirewallRule => await powerShell
                .InspectFirewallAsync(plan, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(step)),
        };

    public async ValueTask ApplyAsync(
        WindowsCircleFilesHelperPlan plan,
        WindowsCircleFilesOperationStep step,
        CancellationToken cancellationToken)
    {
#if DEBUG
        if (string.Equals(
                Environment.GetEnvironmentVariable("BALLS_TEST_WINDOWS_HOST_FAILURE_STEP"),
                step.ToString(),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A bounded debug-only hosting failure was injected.");
        }
#endif
        switch (step)
        {
            case WindowsCircleFilesOperationStep.FolderAcl:
                ApplyFolderAcl(plan);
                break;
            case WindowsCircleFilesOperationStep.OwnershipMarker:
                ApplyMarker(plan);
                break;
            case WindowsCircleFilesOperationStep.EncryptedShare:
                await powerShell.CreateShareAsync(plan, cancellationToken).ConfigureAwait(false);
                break;
            case WindowsCircleFilesOperationStep.PrivateFirewallRule:
                await powerShell.CreateFirewallAsync(plan, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step));
        }
    }

    public async ValueTask RollbackAsync(
        WindowsCircleFilesHelperPlan plan,
        WindowsCircleFilesOperationStep step,
        CancellationToken cancellationToken)
    {
        switch (step)
        {
            case WindowsCircleFilesOperationStep.PrivateFirewallRule:
                await powerShell.RemoveFirewallAsync(plan, cancellationToken).ConfigureAwait(false);
                break;
            case WindowsCircleFilesOperationStep.EncryptedShare:
                await powerShell.RemoveShareAsync(plan, cancellationToken).ConfigureAwait(false);
                break;
            case WindowsCircleFilesOperationStep.OwnershipMarker:
                RemoveMarker(plan);
                break;
            case WindowsCircleFilesOperationStep.FolderAcl:
                RollbackFolderAcl(plan);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step));
        }
    }

    private static WindowsCircleFilesOwnedState InspectFolderAcl(WindowsCircleFilesHelperPlan plan)
    {
        var folder = plan.PublicPlan.FolderPath;
        if (!Directory.Exists(folder))
        {
            return WindowsCircleFilesOwnedState.Missing;
        }

        var journal = ReadJournal(plan);
        if (journal is null)
        {
            return WindowsCircleFilesOwnedState.Missing;
        }

        var currentSddl = GetCurrentSddl(folder);
        return string.Equals(currentSddl, GetDesiredSddl(plan.OwnerSid), StringComparison.Ordinal)
            || string.Equals(currentSddl, journal.PreMutationSddl, StringComparison.Ordinal)
            ? WindowsCircleFilesOwnedState.Owned
            : WindowsCircleFilesOwnedState.Collision;
    }

    private static WindowsCircleFilesOwnedState InspectMarker(WindowsCircleFilesHelperPlan plan)
    {
        var markerPath = Path.Combine(
            plan.PublicPlan.FolderPath,
            WindowsCircleFilesOwnershipMarker.FileName);
        if (!File.Exists(markerPath))
        {
            return WindowsCircleFilesOwnedState.Missing;
        }

        var expected = WindowsCircleFilesOwnershipMarker.Create(
            plan.PublicPlan.OwnershipId,
            plan.Request,
            plan.PublicPlan.FolderPath,
            plan.OwnerSid);
        return string.Equals(File.ReadAllText(markerPath), expected, StringComparison.Ordinal)
            ? WindowsCircleFilesOwnedState.Owned
            : WindowsCircleFilesOwnedState.Collision;
    }

    private static void ApplyFolderAcl(WindowsCircleFilesHelperPlan plan)
    {
        var folder = plan.PublicPlan.FolderPath;
        if (File.Exists(folder))
        {
            throw new InvalidOperationException("The hosting path is a file.");
        }

        var createdDirectories = new List<string>();
        for (var current = folder; !Directory.Exists(current); current = Path.GetDirectoryName(current)
            ?? throw new InvalidOperationException("The hosting folder has no existing ancestor."))
        {
            createdDirectories.Add(current);
        }

        var existed = Directory.Exists(folder);
        Directory.CreateDirectory(folder);
        var entries = Directory.EnumerateFileSystemEntries(folder).ToArray();
        if (entries.Length != 0)
        {
            throw new InvalidOperationException("The hosting folder is no longer empty.");
        }

        var preMutationSddl = GetCurrentSddl(folder);
        var journal = new WindowsCircleFilesJournal(
            CircleFilesHostingContract.Version,
            plan.PublicPlan.OwnershipId,
            plan.PublicPlan.PlanId,
            plan.PublicPlan.FolderPath,
            plan.OwnerSid,
            existed,
            preMutationSddl,
            createdDirectories);
        WriteCreateNew(Path.Combine(folder, JournalFileName), SerializeJournal(journal));
        var security = CreateDesiredSecurity(plan.OwnerSid);
        new DirectoryInfo(folder).SetAccessControl(security);
        ApplyFileSecurity(Path.Combine(folder, JournalFileName), plan.OwnerSid);
    }

    private static void ApplyMarker(WindowsCircleFilesHelperPlan plan)
    {
        var content = WindowsCircleFilesOwnershipMarker.Create(
            plan.PublicPlan.OwnershipId,
            plan.Request,
            plan.PublicPlan.FolderPath,
            plan.OwnerSid);
        WriteCreateNew(
            Path.Combine(plan.PublicPlan.FolderPath, WindowsCircleFilesOwnershipMarker.FileName),
            content);
        ApplyFileSecurity(
            Path.Combine(plan.PublicPlan.FolderPath, WindowsCircleFilesOwnershipMarker.FileName),
            plan.OwnerSid);
    }

    private static void RemoveMarker(WindowsCircleFilesHelperPlan plan)
    {
        if (InspectMarker(plan) != WindowsCircleFilesOwnedState.Owned)
        {
            throw new CircleFilesHostingException(
                "hosting_ownership_collision",
                "The folder ownership marker changed and was left untouched.");
        }

        File.Delete(Path.Combine(plan.PublicPlan.FolderPath, WindowsCircleFilesOwnershipMarker.FileName));
    }

    private static void RollbackFolderAcl(WindowsCircleFilesHelperPlan plan)
    {
        var journal = ReadJournal(plan)
            ?? throw new CircleFilesHostingException(
                "hosting_ownership_collision",
                "The folder operation journal changed and was left untouched.");
        var folder = plan.PublicPlan.FolderPath;
        var currentSddl = GetCurrentSddl(folder);
        if (!string.Equals(currentSddl, GetDesiredSddl(plan.OwnerSid), StringComparison.Ordinal)
            && !string.Equals(currentSddl, journal.PreMutationSddl, StringComparison.Ordinal))
        {
            throw new CircleFilesHostingException(
                "hosting_ownership_collision",
                "The folder ACL changed and was left untouched.");
        }

        if (journal.TargetExisted)
        {
            var security = new DirectorySecurity();
            security.SetSecurityDescriptorSddlForm(journal.PreMutationSddl);
            new DirectoryInfo(folder).SetAccessControl(security);
        }

        var journalPath = Path.Combine(folder, JournalFileName);
        File.Delete(journalPath);
        foreach (var directory in journal.CreatedDirectories)
        {
            if (!Directory.Exists(directory)
                || Directory.EnumerateFileSystemEntries(directory).Any())
            {
                continue;
            }

            Directory.Delete(directory);
        }
    }

    private static DirectorySecurity CreateDesiredSecurity(string ownerSid)
    {
        var owner = new SecurityIdentifier(ownerSid);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        const FileSystemRights rights = FileSystemRights.FullControl;
        const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        security.AddAccessRule(new FileSystemAccessRule(
            owner, rights, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            system, rights, inheritance, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    private static string GetDesiredSddl(string ownerSid) =>
        CreateDesiredSecurity(ownerSid).GetSecurityDescriptorSddlForm(
            AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);

    private static string GetCurrentSddl(string folder) =>
        new DirectoryInfo(folder).GetAccessControl(
                AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access)
            .GetSecurityDescriptorSddlForm(
                AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);

    private static void ApplyFileSecurity(string path, string ownerSid)
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

    private static WindowsCircleFilesJournal? ReadJournal(WindowsCircleFilesHelperPlan plan)
    {
        var path = Path.Combine(plan.PublicPlan.FolderPath, JournalFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var journal = JsonSerializer.Deserialize<WindowsCircleFilesJournal>(File.ReadAllText(path));
            return journal is not null
                && journal.ContractVersion == CircleFilesHostingContract.Version
                && journal.OwnershipId == plan.PublicPlan.OwnershipId
                && journal.PlanId == plan.PublicPlan.PlanId
                && journal.FolderPath.Equals(plan.PublicPlan.FolderPath, StringComparison.OrdinalIgnoreCase)
                && journal.OwnerSid == plan.OwnerSid
                && !string.IsNullOrWhiteSpace(journal.PreMutationSddl)
                && journal.CreatedDirectories is not null
                && journal.CreatedDirectories.All(directory =>
                    directory.Equals(plan.PublicPlan.FolderPath, StringComparison.OrdinalIgnoreCase)
                    || IsParentOf(directory, plan.PublicPlan.FolderPath))
                    ? journal
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static bool IsOwnedJournalContent(
        string? content,
        string ownershipId,
        string planId,
        string folderPath,
        string ownerSid)
    {
        if (content is null)
        {
            return false;
        }

        try
        {
            var journal = JsonSerializer.Deserialize<WindowsCircleFilesJournal>(content);
            return journal is not null
                && journal.ContractVersion == CircleFilesHostingContract.Version
                && journal.OwnershipId == ownershipId
                && journal.PlanId == planId
                && journal.FolderPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase)
                && journal.OwnerSid == ownerSid
                && !string.IsNullOrWhiteSpace(journal.PreMutationSddl)
                && journal.CreatedDirectories is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsParentOf(string possibleParent, string path) =>
        path.StartsWith(
            possibleParent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static string SerializeJournal(WindowsCircleFilesJournal journal) =>
        JsonSerializer.Serialize(journal) + "\n";

    private static void WriteCreateNew(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private sealed record WindowsCircleFilesJournal(
        int ContractVersion,
        string OwnershipId,
        string PlanId,
        string FolderPath,
        string OwnerSid,
        bool TargetExisted,
        string PreMutationSddl,
        IReadOnlyList<string> CreatedDirectories);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsCircleFilesPowerShell
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private const int MaximumOutputCharacters = 16 * 1024;

    internal async ValueTask<WindowsCircleFilesOwnedState> InspectShareAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken) =>
        ParseState(await InvokeAsync("InspectShare", plan, cancellationToken).ConfigureAwait(false));

    internal async ValueTask CreateShareAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken) =>
        _ = await InvokeAsync("CreateShare", plan, cancellationToken).ConfigureAwait(false);

    internal async ValueTask RemoveShareAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken) =>
        _ = await InvokeAsync("RemoveShare", plan, cancellationToken).ConfigureAwait(false);

    internal async ValueTask<WindowsCircleFilesOwnedState> InspectFirewallAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken) =>
        ParseState(await InvokeAsync("InspectFirewall", plan, cancellationToken).ConfigureAwait(false));

    internal async ValueTask CreateFirewallAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken) =>
        _ = await InvokeAsync("CreateFirewall", plan, cancellationToken).ConfigureAwait(false);

    internal async ValueTask RemoveFirewallAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken) =>
        _ = await InvokeAsync("RemoveFirewall", plan, cancellationToken).ConfigureAwait(false);

    private static WindowsCircleFilesOwnedState ParseState(string json)
    {
        using var document = JsonDocument.Parse(json);
        var state = document.RootElement.GetProperty("State").GetString();
        return state switch
        {
            "Missing" => WindowsCircleFilesOwnedState.Missing,
            "Owned" => WindowsCircleFilesOwnedState.Owned,
            "Collision" => WindowsCircleFilesOwnedState.Collision,
            _ => throw new InvalidDataException("The Windows helper returned an invalid state."),
        };
    }

    private static async Task<string> InvokeAsync(
        string command,
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken)
    {
        var executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
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
            Path = plan.PublicPlan.FolderPath,
            plan.PublicPlan.ShareName,
            plan.PublicPlan.FirewallRuleName,
            Description = $"Balls owned v1 {plan.PublicPlan.OwnershipId}",
            OwnerSid = plan.OwnerSid,
        });
        return await RunAsync(startInfo, input, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> RunAsync(
        ProcessStartInfo startInfo,
        string input,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException();
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new IOException("The fixed Windows hosting command could not start.", exception);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token).ConfigureAwait(false);
        process.StandardInput.Close();
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await Task.WhenAll(process.WaitForExitAsync(timeout.Token), outputTask, errorTask)
            .ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (output.Length + error.Length > MaximumOutputCharacters || process.ExitCode != 0)
        {
            throw new IOException("The fixed Windows hosting command failed.");
        }

        return output;
    }

    internal const string Script =
        """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        $request = [Console]::In.ReadToEnd() | Microsoft.PowerShell.Utility\ConvertFrom-Json
        $description = [string]$request.Description

        function Get-ShareState {
            $share = SmbShare\Get-SmbShare -Name ([string]$request.ShareName) -ErrorAction SilentlyContinue
            if ($null -eq $share) { return 'Missing' }
            $access = @(SmbShare\Get-SmbShareAccess -Name ([string]$request.ShareName) -ErrorAction Stop)
            $accessSid = $null
            if ($access.Count -eq 1) {
                try { $accessSid = ([System.Security.Principal.NTAccount]$access[0].AccountName).Translate([System.Security.Principal.SecurityIdentifier]).Value } catch { $accessSid = $null }
            }
            $ownedAccess = $access.Count -eq 1 -and [string]$accessSid -eq [string]$request.OwnerSid -and [string]$access[0].AccessControlType -eq 'Allow' -and [string]$access[0].AccessRight -eq 'Full'
            if ([string]$share.Path -eq [string]$request.Path -and [string]$share.Description -eq $description -and [bool]$share.EncryptData -and $ownedAccess) { return 'Owned' }
            return 'Collision'
        }

        function Get-FirewallState {
            $rule = NetSecurity\Get-NetFirewallRule -Name ([string]$request.FirewallRuleName) -ErrorAction SilentlyContinue
            if ($null -eq $rule) { return 'Missing' }
            $port = $rule | NetSecurity\Get-NetFirewallPortFilter -ErrorAction Stop
            $address = $rule | NetSecurity\Get-NetFirewallAddressFilter -ErrorAction Stop
            $service = $rule | NetSecurity\Get-NetFirewallServiceFilter -ErrorAction Stop
            $owned = [string]$rule.Description -eq $description -and [string]$rule.Direction -eq 'Inbound' -and [string]$rule.Action -eq 'Allow' -and [string]$rule.Enabled -eq 'True' -and [string]$rule.Profile -eq 'Private' -and [string]$port.Protocol -in @('TCP','6') -and [string]$port.LocalPort -eq '445' -and [string]$address.RemoteAddress -eq 'LocalSubnet' -and [string]$service.Service -eq 'LanmanServer'
            if ($owned) { return 'Owned' }
            return 'Collision'
        }

        switch ([string]$request.Command) {
            'InspectShare' { $state = Get-ShareState }
            'CreateShare' {
                if ((Get-ShareState) -ne 'Missing') { throw 'share collision' }
                SmbShare\New-SmbShare -Name ([string]$request.ShareName) -Path ([string]$request.Path) -Description $description -FullAccess ([string]$request.OwnerSid) -EncryptData $true -FolderEnumerationMode AccessBased -CachingMode None -ErrorAction Stop | Out-Null
                $state = Get-ShareState
            }
            'RemoveShare' {
                if ((Get-ShareState) -ne 'Owned') { throw 'share ownership changed' }
                SmbShare\Remove-SmbShare -Name ([string]$request.ShareName) -Force -ErrorAction Stop
                $state = 'Missing'
            }
            'InspectFirewall' { $state = Get-FirewallState }
            'CreateFirewall' {
                if ((Get-FirewallState) -ne 'Missing') { throw 'firewall collision' }
                NetSecurity\New-NetFirewallRule -Name ([string]$request.FirewallRuleName) -DisplayName ('Balls Circle Files ' + [string]$request.ShareName) -Group 'Balls' -Description $description -Enabled True -Profile Private -Direction Inbound -Action Allow -Protocol TCP -LocalPort 445 -RemoteAddress LocalSubnet -Service LanmanServer -ErrorAction Stop | Out-Null
                $state = Get-FirewallState
            }
            'RemoveFirewall' {
                if ((Get-FirewallState) -ne 'Owned') { throw 'firewall ownership changed' }
                NetSecurity\Remove-NetFirewallRule -Name ([string]$request.FirewallRuleName) -ErrorAction Stop
                $state = 'Missing'
            }
            default { throw 'unsupported command' }
        }
        [PSCustomObject]@{ State = $state } | Microsoft.PowerShell.Utility\ConvertTo-Json -Compress
        """;
}
