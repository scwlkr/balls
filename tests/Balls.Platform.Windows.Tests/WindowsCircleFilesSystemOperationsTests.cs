using System.Runtime.Versioning;
using System.Security.AccessControl;
using Balls.Platform;
using Balls.Platform.Windows;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("OSIntegration")]
[SupportedOSPlatform("windows")]
public sealed class WindowsCircleFilesSystemOperationsTests
{
    [TestMethod]
    public async Task Final_host_removal_preserves_an_empty_contributed_folder()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows ACL integration requires Windows.");
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "balls-hosting-tests", Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "CircleFiles");
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(folder);
            var originalSddl = new DirectoryInfo(folder).GetAccessControl(
                    AccessControlSections.Owner
                    | AccessControlSections.Group
                    | AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(
                    AccessControlSections.Owner
                    | AccessControlSections.Group
                    | AccessControlSections.Access);
            Directory.Delete(folder);
            var plan = CreatePlan(folder);
            var operations = new WindowsCircleFilesSystemOperations();
            await operations.ApplyAsync(
                plan,
                WindowsCircleFilesOperationStep.FolderAcl,
                CancellationToken.None);
            await operations.ApplyAsync(
                plan,
                WindowsCircleFilesOperationStep.OwnershipMarker,
                CancellationToken.None);
            await operations.RollbackAsync(
                plan,
                WindowsCircleFilesOperationStep.OwnershipMarker,
                CancellationToken.None);

            await operations.RollbackFolderAclPreservingFolderAsync(
                plan,
                CancellationToken.None);

            Assert.IsTrue(Directory.Exists(folder));
            Assert.IsFalse(Directory.EnumerateFileSystemEntries(folder).Any());
            var restoredSddl = new DirectoryInfo(folder).GetAccessControl(
                    AccessControlSections.Owner
                    | AccessControlSections.Group
                    | AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(
                    AccessControlSections.Owner
                    | AccessControlSections.Group
                    | AccessControlSections.Access);
            Assert.AreEqual(originalSddl, restoredSddl);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task Host_metadata_removal_preserves_contributed_folder_and_exact_user_file_bytes()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows ACL integration requires Windows.");
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "balls-hosting-tests", Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "CircleFiles");
        Directory.CreateDirectory(root);
        try
        {
            var plan = CreatePlan(folder);
            var operations = new WindowsCircleFilesSystemOperations();

            await operations.ApplyAsync(
                plan,
                WindowsCircleFilesOperationStep.FolderAcl,
                CancellationToken.None);

            Assert.AreEqual(
                WindowsCircleFilesOwnedState.Owned,
                await operations.InspectAsync(
                    plan,
                    WindowsCircleFilesOperationStep.FolderAcl,
                    CancellationToken.None));

            await operations.ApplyAsync(
                plan,
                WindowsCircleFilesOperationStep.OwnershipMarker,
                CancellationToken.None);
            var userBytes = Enumerable.Range(0, 4096)
                .Select(index => (byte)(index % 251))
                .ToArray();
            var userFile = Path.Combine(folder, "user-model.bin");
            await File.WriteAllBytesAsync(userFile, userBytes);
            Assert.AreEqual(
                WindowsCircleFilesOwnedState.Owned,
                await operations.InspectAsync(
                    plan,
                    WindowsCircleFilesOperationStep.OwnershipMarker,
                    CancellationToken.None));

            var markerPath = Path.Combine(folder, ".balls-owned-v1.json");
            var originalMarkerSddl = new FileInfo(markerPath).GetAccessControl()
                .GetSecurityDescriptorSddlForm(
                    AccessControlSections.Owner | AccessControlSections.Access);
            var broadenedMarkerSecurity = new FileInfo(markerPath).GetAccessControl();
            broadenedMarkerSecurity.SetAccessRuleProtection(
                isProtected: false,
                preserveInheritance: true);
            new FileInfo(markerPath).SetAccessControl(broadenedMarkerSecurity);
            Assert.AreEqual(
                WindowsCircleFilesOwnedState.Collision,
                await operations.InspectAsync(
                    plan,
                    WindowsCircleFilesOperationStep.OwnershipMarker,
                    CancellationToken.None));
            var restoredMarkerSecurity = new FileSecurity();
            restoredMarkerSecurity.SetSecurityDescriptorSddlForm(
                originalMarkerSddl,
                AccessControlSections.Owner | AccessControlSections.Access);
            new FileInfo(markerPath).SetAccessControl(restoredMarkerSecurity);

            await operations.RollbackAsync(
                plan,
                WindowsCircleFilesOperationStep.OwnershipMarker,
                CancellationToken.None);

            await operations.RollbackAsync(
                plan,
                WindowsCircleFilesOperationStep.FolderAcl,
                CancellationToken.None);
            Assert.IsTrue(Directory.Exists(folder));
            Assert.IsTrue(File.Exists(userFile));
            CollectionAssert.AreEqual(userBytes, await File.ReadAllBytesAsync(userFile));
            Assert.IsFalse(File.Exists(markerPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static WindowsCircleFilesHelperPlan CreatePlan(string folder)
    {
        var ownerSid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value
            ?? throw new AssertFailedException("The test account has no SID.");
        var request = new CircleFilesHostRequest(
            "019d2a6b-1b66-7d38-9c35-8d64ca8f8901",
            "019d2a6b-1b66-7d38-9c35-8d64ca8f8902",
            "019d2a6b-1b66-7d38-9c35-8d64ca8f8903",
            "019d2a6b-1b66-7d38-9c35-8d64ca8f8904",
            "Company files",
            folder,
            new string('a', 64));
        var publicPlan = new CircleFilesHostPlan(
            1,
            new string('b', 64),
            CircleFilesReadinessProviders.WindowsSmb311,
            folder,
            "balls-test",
            "Balls-SMB-test",
            new string('c', 64),
            false,
            []);
        return new WindowsCircleFilesHelperPlan(publicPlan, request, ownerSid);
    }
}
