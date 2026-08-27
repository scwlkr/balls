using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Balls.Platform;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class RevitServerHandoffBundleTests
{
    [TestMethod]
    public void Bundle_contains_only_the_four_versioned_documents_with_verified_internal_hashes()
    {
        var bundle = RevitServerHandoffBundleFactory.Create(Request(TimeSpan.FromMinutes(12)));

        RevitServerHandoffBundleValidator.Validate(bundle.Content);
        Assert.AreEqual("PASS", bundle.Outcome);
        Assert.AreEqual(Hash(bundle.Content), bundle.Sha256);
        using var archive = Open(bundle.Content);
        CollectionAssert.AreEqual(
            new[] { "README.md", "bundle-manifest.json", "setup-receipt.v1.json", "setup-template.v1.json" },
            archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToArray());

        var template = Read(archive, "setup-template.v1.json");
        var receipt = Read(archive, "setup-receipt.v1.json");
        StringAssert.Contains(template, "2027/Projects");
        Assert.IsFalse(template.Contains(@"D:\RevitServer", StringComparison.Ordinal));
        Assert.IsFalse(template.Contains("BALLS-RS27-LAB", StringComparison.Ordinal));
        StringAssert.Contains(receipt, "BALLS-RS27-LAB");
        StringAssert.Contains(receipt, @"D:\\RevitServer\\2027");
        StringAssert.Contains(receipt, "\"replayProhibited\": true");
    }

    [TestMethod]
    public void Strict_timer_passes_below_thirty_minutes_and_fails_at_the_boundary()
    {
        var passing = RevitServerHandoffBundleFactory.Create(
            Request(TimeSpan.FromMinutes(30) - TimeSpan.FromMilliseconds(1)));
        var failed = RevitServerHandoffBundleFactory.Create(
            Request(TimeSpan.FromMinutes(30), "FAILED"));

        Assert.AreEqual("PASS", passing.Outcome);
        Assert.AreEqual("FAILED", failed.Outcome);
        using var archive = Open(failed.Content);
        var receipt = Read(archive, "setup-receipt.v1.json");
        StringAssert.Contains(receipt, "\"outcome\": \"FAILED\"");
        Assert.IsFalse(receipt.Contains(RevitServerHandoffBundleFactory.PassClaim, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Factory_rejects_receipt_gaps_nonhealthy_checks_and_outcome_mismatch()
    {
        var request = Request(TimeSpan.FromMinutes(10));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RevitServerHandoffBundleFactory.Create(request with { HealthChecks = [] }));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RevitServerHandoffBundleFactory.Create(request with { Outcome = "FAILED" }));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RevitServerHandoffBundleFactory.Create(request with
            {
                HumanInterventionElapsed = TimeSpan.FromMinutes(11),
            }));
    }

    [TestMethod]
    public void Validator_rejects_hash_mismatch_extra_payload_and_machine_replay_in_template()
    {
        var original = RevitServerHandoffBundleFactory.Create(Request(TimeSpan.FromMinutes(10))).Content;
        var members = Members(original);

        var mismatched = new Dictionary<string, byte[]>(members, StringComparer.Ordinal)
        {
            ["README.md"] = Encoding.UTF8.GetBytes("changed\n"),
        };
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RevitServerHandoffBundleValidator.Validate(Zip(mismatched)));

        var extra = new Dictionary<string, byte[]>(members, StringComparer.Ordinal)
        {
            ["installer.exe"] = [1, 2, 3],
        };
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RevitServerHandoffBundleValidator.Validate(Zip(extra)));

        var replay = new Dictionary<string, byte[]>(members, StringComparer.Ordinal);
        replay["setup-template.v1.json"] = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(replay["setup-template.v1.json"])
                .Replace("2027/Projects", @"D:\\RevitServer\\2027\\Projects", StringComparison.Ordinal));
        replay["bundle-manifest.json"] = RewriteManifest(replay);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RevitServerHandoffBundleValidator.Validate(Zip(replay)));
    }

    [TestMethod]
    public void Validator_rejects_sids_credentials_private_circle_material_and_model_data()
    {
        foreach (var excluded in new[]
        {
            "S-1-5-21-123-456-789-1001",
            "password=example",
            "private Circle material",
            "company model data",
        })
        {
            var members = Members(RevitServerHandoffBundleFactory.Create(Request(TimeSpan.FromMinutes(10))).Content);
            members["README.md"] = Encoding.UTF8.GetBytes(ReadText(members["README.md"]) + excluded + "\n");
            members["bundle-manifest.json"] = RewriteManifest(members);
            Assert.ThrowsExactly<InvalidDataException>(() =>
                RevitServerHandoffBundleValidator.Validate(Zip(members)), excluded);
        }
    }

    private static RevitServerHandoffRequest Request(TimeSpan elapsed, string outcome = "PASS")
    {
        var started = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
        return new RevitServerHandoffRequest(
            new RevitServerPackageIdentity(
                "development-20260827T120000Z-0123456789ab",
                "0123456789abcdef0123456789abcdef01234567",
                "balls-0.3.0-alpha.1-canary-windows-x64-0123456789ab.zip",
                "0.3.0-alpha.1",
                new string('b', 64)),
            RevitServerSetupPlanFactory.Create(new RevitServerInspectionSnapshot(
                "BALLS-RS27-LAB",
                "Windows Server 2022 Standard Evaluation",
                20348,
                "Server",
                "D:",
                128L * 1024 * 1024 * 1024,
                @"D:\RevitServer\2027",
                "bounded-snapshot",
                new RevitServerMediaIdentity(
                    "Revit_Server_2027_win_db.sfx.exe",
                    "Autodesk, Inc.",
                    "Autodesk Revit Server 2027",
                    "27.0.4.412",
                    new string('a', 64)),
                false,
                [])),
            [
                new RevitServerHealthCheck(
                    "roles",
                    RevitServerHealthStatus.Healthy,
                    "roles_exact",
                    "Host + Admin are enabled and Accelerator is off."),
            ],
            started,
            started + elapsed,
            elapsed,
            TimeSpan.FromMinutes(2),
            ["Accepted Autodesk terms"],
            outcome);
    }

    private static Dictionary<string, byte[]> Members(byte[] content)
    {
        using var archive = Open(content);
        return archive.Entries.ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using var stream = entry.Open();
                using var output = new MemoryStream();
                stream.CopyTo(output);
                return output.ToArray();
            },
            StringComparer.Ordinal);
    }

    private static byte[] RewriteManifest(Dictionary<string, byte[]> members)
    {
        var document = JsonDocument.Parse(members["bundle-manifest.json"]);
        var files = document.RootElement.GetProperty("files").EnumerateArray()
            .Select(item =>
            {
                var name = item.GetProperty("name").GetString()!;
                var bytes = members[name];
                return new
                {
                    name,
                    schemaVersion = item.GetProperty("schemaVersion").ValueKind == JsonValueKind.Null
                        ? (int?)null
                        : item.GetProperty("schemaVersion").GetInt32(),
                    byteLength = bytes.Length,
                    sha256 = Hash(bytes),
                };
            }).ToArray();
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            bundleSchema = "revit-server-2027-setup-bundle-v1",
            files,
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static ZipArchive Open(byte[] content) =>
        new(new MemoryStream(content, writable: false), ZipArchiveMode.Read);

    private static string Read(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string ReadText(byte[] content) => Encoding.UTF8.GetString(content);

    private static byte[] Zip(Dictionary<string, byte[]> members)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var member in members)
            {
                using var stream = archive.CreateEntry(member.Key).Open();
                stream.Write(member.Value);
            }
        }
        return output.ToArray();
    }

    private static string Hash(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));
}
