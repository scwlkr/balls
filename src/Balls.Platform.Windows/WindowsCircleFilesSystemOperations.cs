using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Balls.Platform;

namespace Balls.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsCircleFilesSystemOperations :
    IWindowsCircleFilesOperations,
    IWindowsCircleFilesHostSessionOperations
{
    internal const string JournalFileName = ".balls-operation-v1.json";
    internal const string FirewallRecoveryFileName = ".balls-firewall-recovery-v1.json";
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
                PrepareFirewallRecovery(plan);
                await powerShell.CreateShareAsync(
                    plan,
                    canRestoreServerManagerSmbFirewall: true,
                    cancellationToken).ConfigureAwait(false);
                CompleteFirewallRecovery(plan);
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
                var canRestoreServerManagerSmbFirewall = HasOwnedFirewallRecovery(plan);
                await powerShell.RemoveShareAsync(
                    plan,
                    canRestoreServerManagerSmbFirewall,
                    cancellationToken).ConfigureAwait(false);
                if (canRestoreServerManagerSmbFirewall)
                {
                    CompleteFirewallRecovery(plan);
                }
                break;
            case WindowsCircleFilesOperationStep.OwnershipMarker:
                RemoveMarker(plan);
                break;
            case WindowsCircleFilesOperationStep.FolderAcl:
                RollbackFolderAcl(plan, preserveFolder: false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step));
        }
    }

    public ValueTask<int> CountOpenSessionsAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken) =>
        powerShell.CountOpenSessionsAsync(plan, cancellationToken);

    public ValueTask TerminateOpenSessionsAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken) =>
        powerShell.TerminateOpenSessionsAsync(plan, cancellationToken);

    public ValueTask RollbackFolderAclPreservingFolderAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RollbackFolderAcl(plan, preserveFolder: true);
        return ValueTask.CompletedTask;
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
        var firewallRecoveryPath = Path.Combine(folder, FirewallRecoveryFileName);
        if (File.Exists(firewallRecoveryPath)
            && !HasOwnedFirewallRecovery(plan))
        {
            return WindowsCircleFilesOwnedState.Collision;
        }
        if (HasDesiredSecurity(folder, plan.OwnerSid)
            && HasDesiredFileSecurity(
                Path.Combine(folder, JournalFileName),
                plan.OwnerSid))
        {
            return WindowsCircleFilesOwnedState.Owned;
        }

        return string.Equals(currentSddl, journal.PreMutationSddl, StringComparison.Ordinal)
            ? WindowsCircleFilesOwnedState.Missing
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
            && HasDesiredFileSecurity(markerPath, plan.OwnerSid)
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

        var existingJournal = Directory.Exists(folder) ? ReadJournal(plan) : null;
        if (existingJournal is not null)
        {
            var currentSddl = GetCurrentSddl(folder);
            if (!string.Equals(currentSddl, existingJournal.PreMutationSddl, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The prior folder operation cannot be resumed safely.");
            }

            ApplyFileSecurity(Path.Combine(folder, JournalFileName), plan.OwnerSid);
            new DirectoryInfo(folder).SetAccessControl(CreateDesiredSecurity(plan.OwnerSid));
            return;
        }

        var existed = Directory.Exists(folder);
        var createdDirectories = new List<string>();
        if (!existed)
        {
            var parent = Path.GetDirectoryName(folder);
            if (parent is null || !Directory.Exists(parent))
            {
                throw new InvalidOperationException(
                    "The dedicated hosting folder requires an existing parent directory.");
            }

            createdDirectories.Add(folder);
        }

        Directory.CreateDirectory(folder);
        var entries = Directory.EnumerateFileSystemEntries(folder).ToArray();
        if (entries.Any(IsReservedMetadataPath))
        {
            throw new InvalidOperationException(
                "The hosting folder contains reserved Balls operation metadata.");
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
        var journalPath = Path.Combine(folder, JournalFileName);
        WriteCreateNew(journalPath, SerializeJournal(journal));
        ApplyFileSecurity(journalPath, plan.OwnerSid);
        new DirectoryInfo(folder).SetAccessControl(CreateDesiredSecurity(plan.OwnerSid));
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

    private static void RollbackFolderAcl(
        WindowsCircleFilesHelperPlan plan,
        bool preserveFolder)
    {
        var journal = ReadJournal(plan)
            ?? throw new CircleFilesHostingException(
                "hosting_ownership_collision",
                "The folder operation journal changed and was left untouched.");
        var folder = plan.PublicPlan.FolderPath;
        var currentSddl = GetCurrentSddl(folder);
        if (!HasDesiredSecurity(folder, plan.OwnerSid)
            && !string.Equals(currentSddl, journal.PreMutationSddl, StringComparison.Ordinal))
        {
            throw new CircleFilesHostingException(
                "hosting_ownership_collision",
                "The folder ACL changed and was left untouched.");
        }

        var firewallRecoveryPath = Path.Combine(folder, FirewallRecoveryFileName);
        if (File.Exists(firewallRecoveryPath))
        {
            if (!HasOwnedFirewallRecovery(plan))
            {
                throw new CircleFilesHostingException(
                    "hosting_ownership_collision",
                    "The firewall recovery witness changed and was left untouched.");
            }
            File.Delete(firewallRecoveryPath);
        }

        RestoreSecurityDescriptor(folder, journal.PreMutationSddl);

        var journalPath = Path.Combine(folder, JournalFileName);
        File.Delete(journalPath);
        if (preserveFolder)
        {
            return;
        }

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

    private static string GetCurrentSddl(string folder) =>
        new DirectoryInfo(folder).GetAccessControl(
                AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access)
            .GetSecurityDescriptorSddlForm(
                AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);

    internal static void PrepareFirewallRecovery(WindowsCircleFilesHelperPlan plan)
    {
        var path = Path.Combine(plan.PublicPlan.FolderPath, FirewallRecoveryFileName);
        if (File.Exists(path))
        {
            if (HasOwnedFirewallRecovery(plan))
            {
                return;
            }
            throw new CircleFilesHostingException(
                "hosting_ownership_collision",
                "The firewall recovery witness changed and was left untouched.");
        }

        WriteCreateNew(path, CreateFirewallRecoveryContent(plan));
        ApplyFileSecurity(path, plan.OwnerSid);
    }

    internal static void CompleteFirewallRecovery(WindowsCircleFilesHelperPlan plan)
    {
        if (!HasOwnedFirewallRecovery(plan))
        {
            throw new CircleFilesHostingException(
                "hosting_ownership_collision",
                "The firewall recovery witness changed and was left untouched.");
        }
        File.Delete(Path.Combine(plan.PublicPlan.FolderPath, FirewallRecoveryFileName));
    }

    internal static bool HasOwnedFirewallRecovery(WindowsCircleFilesHelperPlan plan)
    {
        var path = Path.Combine(plan.PublicPlan.FolderPath, FirewallRecoveryFileName);
        return File.Exists(path)
            && string.Equals(
                File.ReadAllText(path),
                CreateFirewallRecoveryContent(plan),
                StringComparison.Ordinal)
            && HasDesiredFileSecurity(path, plan.OwnerSid);
    }

    private static string CreateFirewallRecoveryContent(WindowsCircleFilesHelperPlan plan) =>
        JsonSerializer.Serialize(new WindowsCircleFilesFirewallRecovery(
            CircleFilesHostingContract.Version,
            plan.PublicPlan.OwnershipId,
            plan.PublicPlan.PlanId,
            plan.PublicPlan.ShareName)) + "\n";

    private static void RestoreSecurityDescriptor(string folder, string sddl)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(
                sddl,
                stringSecurityDescriptorRevision: 1,
                out var securityDescriptor,
                out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            const uint ownerSecurityInformation = 0x00000001;
            const uint groupSecurityInformation = 0x00000002;
            const uint daclSecurityInformation = 0x00000004;
            if (!SetFileSecurity(
                    folder,
                    ownerSecurityInformation | groupSecurityInformation | daclSecurityInformation,
                    securityDescriptor))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            _ = LocalFree(securityDescriptor);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor,
        uint stringSecurityDescriptorRevision,
        out IntPtr securityDescriptor,
        out uint securityDescriptorSize);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileSecurity(
        string fileName,
        uint securityInformation,
        IntPtr securityDescriptor);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static bool HasDesiredSecurity(string folder, string ownerSid)
    {
        var owner = new SecurityIdentifier(ownerSid);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new DirectoryInfo(folder).GetAccessControl(
            AccessControlSections.Owner | AccessControlSections.Access);
        if (!security.AreAccessRulesProtected
            || !owner.Equals(security.GetOwner(typeof(SecurityIdentifier))))
        {
            return false;
        }

        var rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        if (rules.Length != 2)
        {
            return false;
        }

        return rules.All(rule =>
            !rule.IsInherited
            && rule.AccessControlType == AccessControlType.Allow
            && rule.FileSystemRights == FileSystemRights.FullControl
            && rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)
            && rule.PropagationFlags == PropagationFlags.None
            && (owner.Equals(rule.IdentityReference) || system.Equals(rule.IdentityReference)))
            && rules.Any(rule => owner.Equals(rule.IdentityReference))
            && rules.Any(rule => system.Equals(rule.IdentityReference));
    }

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

    private static bool HasDesiredFileSecurity(string path, string ownerSid)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var owner = new SecurityIdentifier(ownerSid);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new FileInfo(path).GetAccessControl(
            AccessControlSections.Owner | AccessControlSections.Access);
        if (!security.AreAccessRulesProtected
            || !owner.Equals(security.GetOwner(typeof(SecurityIdentifier))))
        {
            return false;
        }

        var rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        return rules.Length == 2
            && rules.All(rule =>
                !rule.IsInherited
                && rule.AccessControlType == AccessControlType.Allow
                && rule.FileSystemRights == FileSystemRights.FullControl
                && rule.InheritanceFlags == InheritanceFlags.None
                && rule.PropagationFlags == PropagationFlags.None
                && (owner.Equals(rule.IdentityReference) || system.Equals(rule.IdentityReference)))
            && rules.Any(rule => owner.Equals(rule.IdentityReference))
            && rules.Any(rule => system.Equals(rule.IdentityReference));
    }

    private static WindowsCircleFilesJournal? ReadJournal(WindowsCircleFilesHelperPlan plan)
    {
        var path = Path.Combine(plan.PublicPlan.FolderPath, JournalFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        return ParseOwnedJournal(
            File.ReadAllText(path),
            plan.PublicPlan.OwnershipId,
            plan.PublicPlan.PlanId,
            plan.PublicPlan.FolderPath,
            plan.OwnerSid);
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

        return ParseOwnedJournal(content, ownershipId, planId, folderPath, ownerSid) is not null;
    }

    internal static bool IsReservedMetadataPath(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals(JournalFileName, StringComparison.OrdinalIgnoreCase)
            || name.Equals(FirewallRecoveryFileName, StringComparison.OrdinalIgnoreCase)
            || name.Equals(
                WindowsCircleFilesOwnershipMarker.FileName,
                StringComparison.OrdinalIgnoreCase);
    }

    private static WindowsCircleFilesJournal? ParseOwnedJournal(
        string content,
        string ownershipId,
        string planId,
        string folderPath,
        string ownerSid)
    {
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
                && journal.CreatedDirectories is not null
                && (journal.TargetExisted
                    ? journal.CreatedDirectories.Count == 0
                    : journal.CreatedDirectories.Count == 1
                        && journal.CreatedDirectories[0].Equals(
                            folderPath,
                            StringComparison.OrdinalIgnoreCase))
                    ? journal
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

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

    private sealed record WindowsCircleFilesFirewallRecovery(
        int ContractVersion,
        string OwnershipId,
        string PlanId,
        string ShareName);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsCircleFilesPowerShell
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private const int MaximumOutputCharacters = 16 * 1024;

    internal async ValueTask<WindowsCircleFilesOwnedState> InspectShareAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken) =>
        ParseState(await InvokeAsync(
            WindowsCircleFilesPowerShellCommand.InspectShare,
            plan,
            cancellationToken).ConfigureAwait(false));

    internal async ValueTask CreateShareAsync(
        WindowsCircleFilesHelperPlan plan,
        bool canRestoreServerManagerSmbFirewall,
        CancellationToken cancellationToken) =>
        _ = await InvokeAsync(
            WindowsCircleFilesPowerShellCommand.CreateShare,
            plan,
            cancellationToken,
            canRestoreServerManagerSmbFirewall).ConfigureAwait(false);

    internal async ValueTask RemoveShareAsync(
        WindowsCircleFilesHelperPlan plan,
        bool canRestoreServerManagerSmbFirewall,
        CancellationToken cancellationToken) =>
        _ = await InvokeAsync(
            WindowsCircleFilesPowerShellCommand.RemoveShare,
            plan,
            cancellationToken,
            canRestoreServerManagerSmbFirewall).ConfigureAwait(false);

    internal async ValueTask<WindowsCircleFilesOwnedState> InspectFirewallAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken) =>
        ParseState(await InvokeAsync(
            WindowsCircleFilesPowerShellCommand.InspectFirewall,
            plan,
            cancellationToken).ConfigureAwait(false));

    internal async ValueTask CreateFirewallAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken) =>
        _ = await InvokeAsync(
            WindowsCircleFilesPowerShellCommand.CreateFirewall,
            plan,
            cancellationToken).ConfigureAwait(false);

    internal async ValueTask RemoveFirewallAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken) =>
        _ = await InvokeAsync(
            WindowsCircleFilesPowerShellCommand.RemoveFirewall,
            plan,
            cancellationToken).ConfigureAwait(false);

    internal async ValueTask<int> CountOpenSessionsAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await InvokeAsync(
            WindowsCircleFilesPowerShellCommand.CountOpenSessions,
            plan,
            cancellationToken).ConfigureAwait(false));
        return document.RootElement.GetProperty("Count").GetInt32();
    }

    internal async ValueTask TerminateOpenSessionsAsync(
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken) =>
        _ = await InvokeAsync(
            WindowsCircleFilesPowerShellCommand.TerminateOpenSessions,
            plan,
            cancellationToken).ConfigureAwait(false);

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
        WindowsCircleFilesPowerShellCommand command,
        WindowsCircleFilesHelperPlan plan,
        CancellationToken cancellationToken,
        bool canRestoreServerManagerSmbFirewall = false)
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
            Command = command switch
            {
                WindowsCircleFilesPowerShellCommand.InspectShare => "InspectShare",
                WindowsCircleFilesPowerShellCommand.CreateShare => "CreateShare",
                WindowsCircleFilesPowerShellCommand.RemoveShare => "RemoveShare",
                WindowsCircleFilesPowerShellCommand.InspectFirewall => "InspectFirewall",
                WindowsCircleFilesPowerShellCommand.CreateFirewall => "CreateFirewall",
                WindowsCircleFilesPowerShellCommand.RemoveFirewall => "RemoveFirewall",
                WindowsCircleFilesPowerShellCommand.CountOpenSessions => "CountOpenSessions",
                WindowsCircleFilesPowerShellCommand.TerminateOpenSessions => "TerminateOpenSessions",
                _ => throw new ArgumentOutOfRangeException(nameof(command)),
            },
            Path = plan.PublicPlan.FolderPath,
            plan.PublicPlan.ShareName,
            plan.PublicPlan.FirewallRuleName,
            Description = $"Balls owned v1 {plan.PublicPlan.OwnershipId}",
            OwnerSid = plan.OwnerSid,
            CanRestoreServerManagerSmbFirewall = canRestoreServerManagerSmbFirewall,
        });
        return await RunAsync(startInfo, input, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> RunAsync(
        ProcessStartInfo startInfo,
        string input,
        CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedWindowsInspectionProcessRunner.RunWithInputAsync(
                startInfo,
                input,
                Timeout,
                MaximumOutputCharacters,
                cancellationToken).ConfigureAwait(false);
        }
        catch (WindowsInspectionException exception)
        {
            throw new IOException("The fixed Windows hosting command failed.", exception);
        }
    }

    internal const string Script =
        """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        $request = [Console]::In.ReadToEnd() | Microsoft.PowerShell.Utility\ConvertFrom-Json
        $description = [string]$request.Description
        $ownerAccount = ([System.Security.Principal.SecurityIdentifier]([string]$request.OwnerSid)).Translate([System.Security.Principal.NTAccount]).Value

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

        function Restore-ServerManagerSmbFirewallBaseline {
            $rule = NetSecurity\Get-NetFirewallRule -Name 'FileServer-ServerManager-SMB-TCP-In' -ErrorAction SilentlyContinue
            if ($null -ne $rule -and [string]$rule.Enabled -eq 'True') {
                if (-not [bool]$request.CanRestoreServerManagerSmbFirewall) { throw 'built-in smb firewall baseline changed' }
                $rule | NetSecurity\Disable-NetFirewallRule -ErrorAction Stop
            }
        }

        function Test-OwnedOpenPath([string]$path) {
            $root = ([string]$request.Path).TrimEnd('\')
            return $path.Equals($root, [System.StringComparison]::OrdinalIgnoreCase) -or $path.StartsWith($root + '\', [System.StringComparison]::OrdinalIgnoreCase)
        }

        function Get-OwnedOpenFiles {
            $shareState = Get-ShareState
            if ($shareState -eq 'Missing') { return @() }
            if ($shareState -ne 'Owned') { throw 'share ownership changed' }
            $allOpen = @(SmbShare\Get-SmbOpenFile -IncludeHidden -ErrorAction Stop | Microsoft.PowerShell.Utility\Select-Object -First 1001)
            if ($allOpen.Count -gt 1000) { throw 'open file inspection exceeded limit' }
            return @($allOpen | Where-Object { Test-OwnedOpenPath ([string]$_.Path) })
        }

        switch ([string]$request.Command) {
            'InspectShare' { $state = Get-ShareState }
            'CreateShare' {
                if ((Get-ShareState) -ne 'Missing') { throw 'share collision' }
                $serverManagerSmbRuleName = 'FileServer-ServerManager-SMB-TCP-In'
                $serverManagerSmbRule = NetSecurity\Get-NetFirewallRule -Name $serverManagerSmbRuleName -ErrorAction SilentlyContinue
                $serverManagerSmbRuleWasEnabled = $null -ne $serverManagerSmbRule -and [string]$serverManagerSmbRule.Enabled -eq 'True'
                if ($serverManagerSmbRuleWasEnabled) { throw 'unsafe built-in public smb rule enabled' }
                SmbShare\New-SmbShare -Name ([string]$request.ShareName) -Path ([string]$request.Path) -Description $description -FullAccess $ownerAccount -EncryptData $true -FolderEnumerationMode AccessBased -CachingMode None -ErrorAction Stop | Out-Null
                Restore-ServerManagerSmbFirewallBaseline
                $state = Get-ShareState
            }
            'RemoveShare' {
                if ((Get-ShareState) -ne 'Owned') { throw 'share ownership changed' }
                Restore-ServerManagerSmbFirewallBaseline
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
            'CountOpenSessions' { $count = @(@(Get-OwnedOpenFiles) | Select-Object -ExpandProperty SessionId -Unique).Count }
            'TerminateOpenSessions' { @(Get-OwnedOpenFiles) | ForEach-Object { SmbShare\Close-SmbOpenFile -FileId $_.FileId -Force -ErrorAction Stop }; $count = @(@(Get-OwnedOpenFiles) | Select-Object -ExpandProperty SessionId -Unique).Count }
            default { throw 'unsupported command' }
        }
        $result = if ($null -ne $count) { [PSCustomObject]@{ Count = [int]$count } } else { [PSCustomObject]@{ State = $state } }
        $result | Microsoft.PowerShell.Utility\ConvertTo-Json -Compress
        """;
}

internal enum WindowsCircleFilesPowerShellCommand
{
    InspectShare,
    CreateShare,
    RemoveShare,
    InspectFirewall,
    CreateFirewall,
    RemoveFirewall,
    CountOpenSessions,
    TerminateOpenSessions,
}
