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
        var disconnected = await mapper.InspectAsync(Request, CancellationToken.None);
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
        operations.ShareWitness = null;

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

    [DataRow("tampered")]
    [DataRow("wrong-secret")]
    [DataRow("wrong-grant")]
    [DataRow("wrong-owner")]
    [DataRow("wrong-generation")]
    [DataRow("oversized")]
    [TestMethod]
    public async Task Invalid_or_cross_grant_witness_is_rejected_and_exact_resources_are_rolled_back(
        string failure)
    {
        var operations = new StubOperations { AvailableLetters = ["M"] };
        operations.ShareWitness = failure switch
        {
            "tampered" => TamperedWitness(),
            "wrong-secret" => WindowsCircleFilesShareWitness.CreateForMapping(
                Request,
                Encoding.UTF8.GetBytes("Another-Wrong-Grant-Secret-43!")),
            "wrong-grant" => WindowsCircleFilesShareWitness.CreateForMapping(
                Request with { GrantId = "66666666-6666-6666-6666-666666666666" },
                Secret),
            "wrong-owner" => WindowsCircleFilesShareWitness.CreateForMapping(
                Request with { GrantOwnershipId = new string('b', 64) },
                Secret),
            "wrong-generation" => WindowsCircleFilesShareWitness.CreateForMapping(
                Request with { Generation = 2 },
                Secret),
            "oversized" => new byte[WindowsCircleFilesShareWitness.MaximumBytes + 1],
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        var mapper = new WindowsCircleFilesMemberMapper(operations);
        var plan = await mapper.PreviewAsync(Request, CancellationToken.None);

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
    public async Task Already_mapped_share_revalidates_the_authenticated_grant_witness()
    {
        var operations = new StubOperations { AvailableLetters = ["M"] };
        var mapper = new WindowsCircleFilesMemberMapper(operations);
        var plan = await mapper.PreviewAsync(Request, CancellationToken.None);
        _ = await mapper.MapAsync(Request, plan.PlanId, Secret, CancellationToken.None);
        operations.ShareWitness = TamperedWitness();
        operations.Events.Clear();

        var error = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => mapper.MapAsync(Request, plan.PlanId, Secret, CancellationToken.None).AsTask());

        Assert.AreEqual("mapping_share_identity_mismatch", error.Code);
        Assert.AreEqual(plan.UncPath, operations.Mapping);
        Assert.IsNotNull(operations.Credential);
        CollectionAssert.AreEqual(new[] { "share:validate" }, operations.Events.ToArray());
    }

    [TestMethod]
    public async Task Failed_exact_rollback_is_typed_and_preserves_the_ownership_witness()
    {
        var operations = new StubOperations
        {
            AvailableLetters = ["M"],
            DriveDeleteFailure = new IOException("Injected exact cleanup failure."),
        };
        operations.ShareWitness = null;
        var mapper = new WindowsCircleFilesMemberMapper(operations);
        var plan = await mapper.PreviewAsync(Request, CancellationToken.None);

        var error = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => mapper.MapAsync(Request, plan.PlanId, Secret, CancellationToken.None).AsTask());

        Assert.AreEqual("mapping_recovery_incomplete", error.Code);
        Assert.AreEqual(plan.UncPath, operations.Mapping);
        Assert.IsNotNull(operations.Credential);
        CollectionAssert.AreEqual(
            new[]
            {
                "endpoint:probe", "credential:save", "drive:map", "share:validate",
                "drive:delete",
            },
            operations.Events.ToArray());
    }

    [TestMethod]
    public async Task Failed_reconnect_rollback_is_typed()
    {
        var operations = new StubOperations { AvailableLetters = ["M"] };
        var mapper = new WindowsCircleFilesMemberMapper(operations);
        var plan = await mapper.PreviewAsync(Request, CancellationToken.None);
        _ = await mapper.MapAsync(Request, plan.PlanId, Secret, CancellationToken.None);
        operations.DriveAccessible = false;
        operations.ShareWitness = null;
        operations.DisconnectFailure = new IOException("Injected session cleanup failure.");
        operations.Events.Clear();

        var error = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => mapper.MapAsync(Request, plan.PlanId, Secret, CancellationToken.None).AsTask());

        Assert.AreEqual("mapping_recovery_incomplete", error.Code);
        CollectionAssert.AreEqual(
            new[] { "endpoint:probe", "drive:reconnect", "share:validate", "drive:disconnect" },
            operations.Events.ToArray());
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
            () => mapper.UnmapAsync(Request, CancellationToken.None).AsTask());
        Assert.AreEqual("mapping_resource_collision", collision.Code);

        operations.Mapping = plan.UncPath;
        operations.Events.Clear();
        var result = await mapper.UnmapAsync(Request, CancellationToken.None);
        Assert.AreEqual("unmapped", result.Status);
        CollectionAssert.AreEqual(
            new[] { "drive:delete", "label:delete", "credential:delete" },
            operations.Events.ToArray());
    }

    [TestMethod]
    public async Task Map_and_unmap_refuse_a_foreign_drive_even_when_its_unc_matches()
    {
        var operations = new StubOperations { AvailableLetters = ["M"] };
        var mapper = new WindowsCircleFilesMemberMapper(operations);
        var plan = await mapper.PreviewAsync(Request, CancellationToken.None);
        operations.Mapping = plan.UncPath;
        operations.DriveAccessible = true;

        var previewError = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => mapper.PreviewAsync(Request, CancellationToken.None).AsTask());
        Assert.AreEqual("mapping_drive_collision", previewError.Code);

        var mapError = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => mapper.MapAsync(Request, plan.PlanId, Secret, CancellationToken.None).AsTask());
        Assert.AreEqual("mapping_drive_collision", mapError.Code);

        var unmapError = await Assert.ThrowsExactlyAsync<CircleFilesHostingException>(
            () => mapper.UnmapAsync(Request, CancellationToken.None).AsTask());
        Assert.AreEqual("mapping_drive_collision", unmapError.Code);
        Assert.AreEqual(plan.UncPath, operations.Mapping);
        Assert.IsNull(operations.Credential);
        Assert.IsNull(operations.Label);
        Assert.AreEqual(0, operations.Events.Count);
    }

    [TestMethod]
    public async Task Exact_partial_label_is_completed_and_can_be_removed_after_restart()
    {
        var operations = new StubOperations { AvailableLetters = ["M"] };
        var mapper = new WindowsCircleFilesMemberMapper(operations);
        var plan = await mapper.PreviewAsync(Request, CancellationToken.None);
        operations.Credential = new(
            plan.CredentialTarget, Request.AccountName, plan.OwnershipId, Secret.ToArray());
        operations.Mapping = plan.UncPath;
        operations.DriveAccessible = true;
        operations.Label = new("", plan.OwnershipId, plan.OwnershipId);

        var inspection = await mapper.InspectAsync(Request, CancellationToken.None);
        Assert.AreEqual("partial", inspection.Status);

        var result = await mapper.MapAsync(Request, plan.PlanId, Secret, CancellationToken.None);
        Assert.AreEqual("mapped", result.Status);
        Assert.AreEqual(plan.FriendlyName, operations.Label.FriendlyName);
        Assert.AreEqual(plan.OwnershipId, operations.Label.OwnershipId);

        var unmap = await mapper.UnmapAsync(Request, CancellationToken.None);
        Assert.AreEqual("unmapped", unmap.Status);
        Assert.IsNull(operations.Label);
    }

    [TestMethod]
    public void Native_password_buffer_is_explicitly_null_terminated()
    {
        var chars = WindowsCircleFilesMappingOperations.ToNullTerminatedPassword(Secret);
        try
        {
            Assert.AreEqual('\0', chars[^1]);
            Assert.AreEqual(Encoding.UTF8.GetString(Secret), new string(chars, 0, chars.Length - 1));
        }
        finally
        {
            Array.Clear(chars);
        }
    }

    [TestMethod]
    public void Native_share_witness_reader_accepts_only_bounded_nonempty_content()
    {
        var directory = Path.Combine(Path.GetTempPath(), "balls-witness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "witness.json");
            var operations = new WindowsCircleFilesMappingOperations();
            var expected = new byte[WindowsCircleFilesShareWitness.MaximumBytes];
            expected[^1] = 42;
            File.WriteAllBytes(path, expected);

            CollectionAssert.AreEqual(
                expected,
                operations.ReadShareEntry(
                    directory,
                    "witness.json",
                    WindowsCircleFilesShareWitness.MaximumBytes));

            File.WriteAllBytes(path, new byte[WindowsCircleFilesShareWitness.MaximumBytes + 1]);
            Assert.ThrowsExactly<InvalidDataException>(() => operations.ReadShareEntry(
                directory,
                "witness.json",
                WindowsCircleFilesShareWitness.MaximumBytes));

            File.WriteAllBytes(path, Array.Empty<byte>());
            Assert.ThrowsExactly<InvalidDataException>(() => operations.ReadShareEntry(
                directory,
                "witness.json",
                WindowsCircleFilesShareWitness.MaximumBytes));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => operations.ReadShareEntry(
                directory,
                "witness.json",
                WindowsCircleFilesShareWitness.MaximumBytes + 1));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] TamperedWitness()
    {
        var witness = WindowsCircleFilesShareWitness.CreateForMapping(Request, Secret);
        witness[^3] = witness[^3] == (byte)'a' ? (byte)'b' : (byte)'a';
        return witness;
    }

    private sealed class StubOperations : IWindowsCircleFilesMappingOperations
    {
        public IReadOnlyList<string> AvailableLetters { get; set; } = [];
        public string? Mapping { get; set; }
        public WindowsCircleFilesStoredCredential? Credential { get; set; }
        public WindowsCircleFilesStoredLabel? Label { get; set; }
        public byte[]? ShareWitness { get; set; } =
            WindowsCircleFilesShareWitness.CreateForMapping(Request, Secret);
        public IOException? EndpointFailure { get; set; }
        public IOException? DriveDeleteFailure { get; set; }
        public IOException? DisconnectFailure { get; set; }
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
            if (DisconnectFailure is not null) throw DisconnectFailure;
            DriveAccessible = false;
        }

        public byte[] ReadShareEntry(string uncPath, string fileName, int maximumBytes)
        {
            Events.Add("share:validate");
            if (fileName != WindowsCircleFilesShareWitness.GetFileName(Request.GrantId, Request.Generation)
                || ShareWitness is null)
            {
                throw new FileNotFoundException("The exact grant witness is absent.");
            }
            if (ShareWitness.Length > maximumBytes)
            {
                throw new InvalidDataException("The exact grant witness exceeded its bounded size.");
            }
            return ShareWitness.ToArray();
        }

        public void SaveLabel(string uncPath, string friendlyName, string ownershipId)
        {
            Events.Add("label:save");
            Label = new(friendlyName, ownershipId);
        }

        public void UnmapDrive(string driveLetter, string expectedUncPath)
        {
            Events.Add("drive:delete");
            if (DriveDeleteFailure is not null) throw DriveDeleteFailure;
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
