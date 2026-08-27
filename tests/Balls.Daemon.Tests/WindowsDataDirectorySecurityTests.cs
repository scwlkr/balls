using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Balls.Platform.Windows;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("OSIntegration")]
public sealed class WindowsDataDirectorySecurityTests
{
    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void State_directory_acl_is_protected_and_limited_to_current_user_and_system()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 state-directory ACL is currently Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var stateDirectory = Path.Combine(directory.Path, "state");
        Directory.CreateDirectory(stateDirectory);

        WindowsDataDirectorySecurity.Prepare(stateDirectory);
        var existingStateFile = Path.Combine(
            stateDirectory,
            "automatic-private-listeners-v1.json");
        File.WriteAllText(existingStateFile, "existing");
        WindowsDataDirectorySecurity.Prepare(stateDirectory);

        var security = new DirectoryInfo(stateDirectory).GetAccessControl(
            AccessControlSections.Access);
        var allowedSids = security
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow)
            .Select(rule => (SecurityIdentifier)rule.IdentityReference)
            .ToHashSet();
        var currentUser = WindowsIdentity.GetCurrent().User;
        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        Assert.IsTrue(security.AreAccessRulesProtected);
        Assert.IsNotNull(currentUser);
        Assert.IsTrue(allowedSids.SetEquals([currentUser, localSystem]));

        var fileSecurity = new FileInfo(existingStateFile).GetAccessControl(
            AccessControlSections.Access);
        var fileAllowedSids = fileSecurity
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow)
            .Select(rule => (SecurityIdentifier)rule.IdentityReference)
            .ToHashSet();
        Assert.IsTrue(fileSecurity.AreAccessRulesProtected);
        Assert.IsTrue(fileAllowedSids.SetEquals([currentUser, localSystem]));
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void Unknown_nonempty_directory_is_rejected_without_rewriting_its_acl()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Phase 1 state-directory ACL is currently Windows-only.");
            return;
        }

        using var directory = new TemporaryDirectory();
        var unrelatedDirectory = Path.Combine(directory.Path, "unrelated");
        Directory.CreateDirectory(unrelatedDirectory);
        var importantFile = Path.Combine(unrelatedDirectory, "important.txt");
        File.WriteAllText(importantFile, "keep me");
        var originalSecurity = new DirectoryInfo(unrelatedDirectory)
            .GetAccessControl(AccessControlSections.Access)
            .GetSecurityDescriptorSddlForm(AccessControlSections.Access);

        Assert.ThrowsExactly<UnauthorizedAccessException>(
            () => WindowsDataDirectorySecurity.Prepare(unrelatedDirectory));

        var currentSecurity = new DirectoryInfo(unrelatedDirectory)
            .GetAccessControl(AccessControlSections.Access)
            .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        Assert.AreEqual(originalSecurity, currentSecurity);
        Assert.AreEqual("keep me", File.ReadAllText(importantFile));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "balls-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
