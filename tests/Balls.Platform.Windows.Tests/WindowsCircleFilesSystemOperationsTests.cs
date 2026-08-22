using System.Runtime.Versioning;
using Balls.Platform;
using Balls.Platform.Windows;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("OSIntegration")]
[SupportedOSPlatform("windows")]
public sealed class WindowsCircleFilesSystemOperationsTests
{
    [TestMethod]
    public async Task Folder_acl_is_recognized_as_exact_owned_state_and_rolls_back_cleanly()
    {
        var root = Path.Combine(Path.GetTempPath(), "balls-hosting-tests", Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "CircleFiles");
        Directory.CreateDirectory(root);
        try
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
            var plan = new WindowsCircleFilesHelperPlan(publicPlan, request, ownerSid);
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

            await operations.RollbackAsync(
                plan,
                WindowsCircleFilesOperationStep.FolderAcl,
                CancellationToken.None);
            Assert.IsFalse(Directory.Exists(folder));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
