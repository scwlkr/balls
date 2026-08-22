using System.Runtime.Versioning;
using System.Text;
using Balls.Platform;
using Balls.Platform.Windows;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("Contract")]
[SupportedOSPlatform("windows")]
public sealed class WindowsCircleFilesMemberMapperTests
{
    private static readonly CircleFilesMemberMappingRequest Request = new(
        "11111111-1111-1111-1111-111111111111",
        "22222222-2222-2222-2222-222222222222",
        "33333333-3333-3333-3333-333333333333",
        "44444444-4444-4444-4444-444444444444",
        "55555555-5555-5555-5555-555555555555",
        "BallsG-444444444444",
        new string('a', 64),
        "read-write",
        1,
        "Monday Files",
        "192.168.50.10",
        "M");

    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("Correct-Horse-Battery-Staple-42!");

    [TestMethod]
    public async Task Preview_discovers_letters_and_refuses_an_existing_letter()
    {
        var operations = new StubOperations { AvailableLetters = ["M", "N"] };
        var mapper = new WindowsCircleFilesMemberMapper(operations);

        var plan = await mapper.PreviewAsync(Request, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "M", "N" }, plan.AvailableDriveLetters.ToArray());
        Assert.AreEqual(@"\\192.168.50.10\balls-333333333333", plan.UncPath);
        Assert.AreEqual("192.168.50.10", plan.CredentialTarget);
        Assert.AreEqual("Monday Files", plan.FriendlyName);

        var discovery = await mapper.PreviewAsync(
            Request with { DriveLetter = string.Empty },
            CancellationToken.None);
        Assert.AreEqual(string.Empty, discovery.DriveLetter);
        CollectionAssert.AreEqual(new[] { "M", "N" }, discovery.AvailableDriveLetters.ToArray());

        operations.AvailableLetters = ["N"];
        var error = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => mapper.PreviewAsync(Request, CancellationToken.None).AsTask());
        Assert.AreEqual("mapping_drive_collision", error.Code);
    }

    [TestMethod]
    public async Task Map_validates_the_exact_share_and_is_idempotent_without_exposing_secret()
    {
        var operations = new StubOperations { AvailableLetters = ["M", "N"] };
        var mapper = new WindowsCircleFilesMemberMapper(operations);
        var plan = await mapper.PreviewAsync(Request, CancellationToken.None);

        var result = await mapper.MapAsync(Request, plan.PlanId, Secret, CancellationToken.None);

        Assert.AreEqual("mapped", result.Status);
        CollectionAssert.AreEqual(
            new[] { "endpoint:probe", "credential:save", "drive:map", "share:validate", "label:save" },
            operations.Events.ToArray());
        Assert.IsFalse(result.ToString()!.Contains(Encoding.UTF8.GetString(Secret), StringComparison.Ordinal));

        operations.Events.Clear();
        var retry = await mapper.MapAsync(Request, plan.PlanId, Secret, CancellationToken.None);
        Assert.AreEqual("already-mapped", retry.Status);
        CollectionAssert.AreEqual(new[] { "share:validate" }, operations.Events.ToArray());

        operations.DriveAccessible = false;
        operations.Events.Clear();
        var disconnected = await mapper.InspectAsync(Request, Secret, CancellationToken.None);
        Assert.AreEqual("partial", disconnected.Status);
        var reconnect = await mapper.MapAsync(Request, plan.PlanId, Secret, CancellationToken.None);
        Assert.AreEqual("already-mapped", reconnect.Status);
        Assert.IsTrue(operations.DriveAccessible);
        CollectionAssert.AreEqual(
            new[] { "endpoint:probe", "drive:reconnect", "share:validate" },
            operations.Events.ToArray());
    }

    [TestMethod]
    public async Task Wrong_share_is_rejected_and_only_new_exact_resources_are_rolled_back()
    {
        var operations = new StubOperations { AvailableLetters = ["M"] };
        var mapper = new WindowsCircleFilesMemberMapper(operations);
        var plan = await mapper.PreviewAsync(Request, CancellationToken.None);
        operations.ShareEntries.Remove($".balls-grant-{Request.GrantId}-g1-v1.json");

        var error = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => mapper.MapAsync(Request, plan.PlanId, Secret, CancellationToken.None).AsTask());

        Assert.AreEqual("mapping_share_identity_mismatch", error.Code);
        CollectionAssert.AreEqual(
            new[]
            {
                "endpoint:probe", "credential:save", "drive:map", "share:validate",
                "drive:delete", "credential:delete",
            },
            operations.Events.ToArray());
        Assert.IsNull(operations.Mapping);
        Assert.IsNull(operations.Credential);
    }

    [TestMethod]
    public async Task Unreachable_share_is_bounded_and_rolls_back_the_exact_new_prefix()
    {
        var operations = new StubOperations
        {
            AvailableLetters = ["M"],
            EndpointFailure = new IOException("Sensitive native network detail."),
        };
        var mapper = new WindowsCircleFilesMemberMapper(operations);
        var plan = await mapper.PreviewAsync(Request, CancellationToken.None);

        var error = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => mapper.MapAsync(Request, plan.PlanId, Secret, CancellationToken.None).AsTask());

        Assert.AreEqual("mapping_endpoint_unreachable", error.Code);
        Assert.IsFalse(error.Message.Contains("Sensitive", StringComparison.Ordinal));
        CollectionAssert.AreEqual(
            new[]
            {
                "endpoint:probe",
            },
            operations.Events.ToArray());
    }

    [TestMethod]
    public async Task Unmap_refuses_repurposed_resources_and_removes_only_exact_owned_state()
    {
        var operations = new StubOperations { AvailableLetters = ["M"] };
        var mapper = new WindowsCircleFilesMemberMapper(operations);
        var plan = await mapper.PreviewAsync(Request, CancellationToken.None);
        _ = await mapper.MapAsync(Request, plan.PlanId, Secret, CancellationToken.None);

        operations.Mapping = @"\\192.168.50.10\someone-elses-share";
        var collision = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => mapper.UnmapAsync(Request, Secret, CancellationToken.None).AsTask());
        Assert.AreEqual("mapping_resource_collision", collision.Code);

        operations.Mapping = plan.UncPath;
        operations.Events.Clear();
        var result = await mapper.UnmapAsync(Request, Secret, CancellationToken.None);
        Assert.AreEqual("unmapped", result.Status);
        CollectionAssert.AreEqual(
            new[] { "drive:delete", "label:delete", "credential:delete" },
            operations.Events.ToArray());
    }

    private sealed class StubOperations : IWindowsCircleFilesMappingOperations
    {
        public IReadOnlyList<string> AvailableLetters { get; set; } = [];
        public string? Mapping { get; set; }
        public WindowsCircleFilesStoredCredential? Credential { get; set; }
        public WindowsCircleFilesStoredLabel? Label { get; set; }
        public HashSet<string> ShareEntries { get; } =
            [".balls-owned-v1.json", $".balls-grant-{Request.GrantId}-g1-v1.json"];
        public IOException? EndpointFailure { get; set; }
        public bool DriveAccessible { get; set; }
        public List<string> Events { get; } = [];

        public IReadOnlyList<string> GetAvailableDriveLetters() => AvailableLetters;
        public string? GetMappedUnc(string driveLetter) => Mapping;
        public bool IsDriveAccessible(string driveLetter) => DriveAccessible;
        public WindowsCircleFilesStoredCredential? GetCredential(string target) => Credential;
        public WindowsCircleFilesStoredLabel? GetLabel(string uncPath) => Label;

        public void ProbeEndpoint(string endpoint)
        {
            Events.Add("endpoint:probe");
            if (EndpointFailure is not null) throw EndpointFailure;
        }

        public void SaveCredential(string target, string accountName, string ownershipId, ReadOnlySpan<byte> secret)
        {
            Events.Add("credential:save");
            Credential = new(target, accountName, ownershipId, secret.ToArray());
        }

        public void MapDrive(string driveLetter, string uncPath, string accountName, ReadOnlySpan<byte> secret)
        {
            Events.Add("drive:map");
            Mapping = uncPath;
            DriveAccessible = true;
        }

        public void ReconnectDrive(string driveLetter, string uncPath, string accountName, ReadOnlySpan<byte> secret)
        {
            Events.Add("drive:reconnect");
            Mapping = uncPath;
            DriveAccessible = true;
        }

        public void DisconnectDriveSession(string driveLetter, string expectedUncPath)
        {
            Events.Add("drive:disconnect");
            DriveAccessible = false;
        }

        public bool ShareEntryExists(string uncPath, string fileName)
        {
            if (fileName.StartsWith(".balls-grant-", StringComparison.Ordinal))
            {
                Events.Add("share:validate");
            }
            return ShareEntries.Contains(fileName);
        }

        public void SaveLabel(string uncPath, string friendlyName, string ownershipId)
        {
            Events.Add("label:save");
            Label = new(friendlyName, ownershipId);
        }

        public void UnmapDrive(string driveLetter, string expectedUncPath)
        {
            Events.Add("drive:delete");
            Mapping = null;
            DriveAccessible = false;
        }

        public void DeleteLabel(string uncPath, string friendlyName, string ownershipId)
        {
            Events.Add("label:delete");
            Label = null;
        }

        public void DeleteCredential(string target, string accountName, string ownershipId)
        {
            Events.Add("credential:delete");
            Credential = null;
        }
    }
}
