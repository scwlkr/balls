using System.Text.Json.Nodes;
using Balls.Platform;
using Balls.Platform.Windows;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class WindowsSmbReadinessInspectorTests
{
    private const string ReadyObservation =
        """
        {
          "System": { "BuildNumber": 26100, "InstallationType": "Client" },
          "Services": { "LanmanServer": "Running", "WindowsFirewall": "Running" },
          "SmbServer": {
            "EnableSMB1Protocol": false,
            "EnableSMB2Protocol": true,
            "Smb2DialectMax": "SMB311",
            "RequireSecuritySignature": true,
            "RejectUnencryptedAccess": true,
            "ShareEncryptionSupported": true,
            "EncryptionCiphers": ["AES_128_GCM", "AES_128_CCM"]
          },
          "SmbClient": { "EnableInsecureGuestLogons": false },
          "Network": { "ConnectedPrivateProfiles": 1 },
          "Firewall": {
            "PrivateEnabled": true,
            "PrivateDefaultInboundAction": "Block",
            "PublicEnabled": true,
            "PublicDefaultInboundAction": "Block",
            "PublicSmbInboundAllowRules": 0
          }
        }
        """;

    [TestMethod]
    public async Task Safe_supported_host_is_ready_with_an_ordered_redacted_report()
    {
        var source = new StubJsonSource(ReadyObservation);
        var inspector = new WindowsSmbReadinessInspector(source);

        var report = await inspector.InspectAsync(CancellationToken.None);

        Assert.AreEqual(CircleFilesReadinessProviders.WindowsSmb311, report.Provider);
        Assert.AreEqual(CircleFilesReadinessStatus.Ready, report.Status);
        CollectionAssert.AreEqual(
            new[]
            {
                "windows-platform",
                "smb-server",
                "smb-dialect",
                "smb1",
                "guest-access",
                "signing",
                "encryption",
                "private-network",
                "firewall-scope",
            },
            report.Checks.Select(check => check.Id).ToArray());
        Assert.IsTrue(report.Checks.All(check => check.Status == CircleFilesReadinessStatus.Ready));
        CollectionAssert.AreEqual(
            new[] { WindowsPowerShellQuery.SmbReadiness },
            source.Queries);
        Assert.IsFalse(report.Checks.Any(check => check.Summary.Contains("Alice", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Every_known_unsafe_prerequisite_makes_the_report_not_ready()
    {
        var cases = new (Action<JsonObject> Mutate, string DiagnosticCode)[]
        {
            (root => root["System"]!["BuildNumber"] = 22631, "windows_version_unsupported"),
            (root => root["System"]!["InstallationType"] = "UnknownEdition", "windows_edition_unsupported"),
            (root => root["Services"]!["LanmanServer"] = "Stopped", "smb_server_unavailable"),
            (root => root["SmbServer"]!["EnableSMB2Protocol"] = false, "smb2_disabled"),
            (root => root["SmbServer"]!["Smb2DialectMax"] = "SMB302", "smb311_unavailable"),
            (root => root["SmbServer"]!["EnableSMB1Protocol"] = true, "smb1_enabled"),
            (root => root["SmbServer"]!["RequireSecuritySignature"] = false, "smb_signing_not_required"),
            (root => root["SmbServer"]!["RejectUnencryptedAccess"] = false, "unencrypted_access_accepted"),
            (root => root["SmbServer"]!["ShareEncryptionSupported"] = false, "share_encryption_unavailable"),
            (root => root["SmbServer"]!["EncryptionCiphers"] = new JsonArray("AES_128_CCM"), "smb311_encryption_cipher_unavailable"),
            (root => root["Network"]!["ConnectedPrivateProfiles"] = 0, "private_network_unavailable"),
            (root => root["Services"]!["WindowsFirewall"] = "Stopped", "windows_firewall_unavailable"),
            (root => root["Firewall"]!["PrivateEnabled"] = false, "private_firewall_disabled"),
            (root => root["Firewall"]!["PublicEnabled"] = false, "public_firewall_disabled"),
            (root => root["Firewall"]!["PrivateDefaultInboundAction"] = "Allow", "private_inbound_not_blocked"),
            (root => root["Firewall"]!["PublicDefaultInboundAction"] = "Allow", "public_inbound_not_blocked"),
            (root => root["Firewall"]!["PublicSmbInboundAllowRules"] = 1, "public_smb_inbound_allowed"),
        };

        foreach (var testCase in cases)
        {
            var root = JsonNode.Parse(ReadyObservation)!.AsObject();
            testCase.Mutate(root);
            var inspector = new WindowsSmbReadinessInspector(new StubJsonSource(root.ToJsonString()));

            var report = await inspector.InspectAsync(CancellationToken.None);

            Assert.AreEqual(
                CircleFilesReadinessStatus.NotReady,
                report.Status,
                $"Expected {testCase.DiagnosticCode} to fail closed.");
            Assert.IsTrue(
                report.Checks.Any(check =>
                    check.Status == CircleFilesReadinessStatus.NotReady
                    && check.Code == testCase.DiagnosticCode),
                $"Expected diagnostic {testCase.DiagnosticCode}.");
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Outbound_client_guest_policy_does_not_change_circle_share_protection(bool enableInsecureGuestLogons)
    {
        var root = JsonNode.Parse(ReadyObservation)!.AsObject();
        root["SmbClient"]!["EnableInsecureGuestLogons"] = enableInsecureGuestLogons;
        var inspector = new WindowsSmbReadinessInspector(new StubJsonSource(root.ToJsonString()));

        var report = await inspector.InspectAsync(CancellationToken.None);
        var guestAccess = report.Checks.Single(check => check.Id == "guest-access");

        Assert.AreEqual(CircleFilesReadinessStatus.Ready, report.Status);
        Assert.AreEqual(CircleFilesReadinessStatus.Ready, guestAccess.Status);
        Assert.AreEqual("guest_access_precluded", guestAccess.Code);
    }

    [TestMethod]
    public async Task Missing_outbound_client_observation_does_not_block_a_secure_circle_share()
    {
        var root = JsonNode.Parse(ReadyObservation)!.AsObject();
        root.Remove("SmbClient");
        var inspector = new WindowsSmbReadinessInspector(new StubJsonSource(root.ToJsonString()));

        var report = await inspector.InspectAsync(CancellationToken.None);
        var guestAccess = report.Checks.Single(check => check.Id == "guest-access");

        Assert.AreEqual(CircleFilesReadinessStatus.Ready, report.Status);
        Assert.AreEqual(CircleFilesReadinessStatus.Ready, guestAccess.Status);
        Assert.AreEqual("guest_access_precluded", guestAccess.Code);
    }

    [TestMethod]
    [DataRow("RequireSecuritySignature")]
    [DataRow("RejectUnencryptedAccess")]
    [DataRow("ShareEncryptionSupported")]
    public async Task Missing_server_guest_protection_fails_closed(string serverProperty)
    {
        var root = JsonNode.Parse(ReadyObservation)!.AsObject();
        root["SmbServer"]!.AsObject().Remove(serverProperty);
        var inspector = new WindowsSmbReadinessInspector(new StubJsonSource(root.ToJsonString()));

        var report = await inspector.InspectAsync(CancellationToken.None);
        var guestAccess = report.Checks.Single(check => check.Id == "guest-access");

        Assert.AreEqual(CircleFilesReadinessStatus.Unknown, report.Status);
        Assert.AreEqual(CircleFilesReadinessStatus.Unknown, guestAccess.Status);
        Assert.AreEqual("guest_access_controls_unknown", guestAccess.Code);
    }

    [TestMethod]
    [DataRow("RequireSecuritySignature")]
    [DataRow("RejectUnencryptedAccess")]
    [DataRow("ShareEncryptionSupported")]
    public async Task Disabled_server_guest_protection_fails_closed(string serverProperty)
    {
        var root = JsonNode.Parse(ReadyObservation)!.AsObject();
        root["SmbServer"]![serverProperty] = false;
        var inspector = new WindowsSmbReadinessInspector(new StubJsonSource(root.ToJsonString()));

        var report = await inspector.InspectAsync(CancellationToken.None);
        var guestAccess = report.Checks.Single(check => check.Id == "guest-access");

        Assert.AreEqual(CircleFilesReadinessStatus.NotReady, report.Status);
        Assert.AreEqual(CircleFilesReadinessStatus.NotReady, guestAccess.Status);
        Assert.AreEqual("guest_access_not_precluded", guestAccess.Code);
    }

    [TestMethod]
    public async Task Missing_forward_unknown_and_malformed_observations_remain_unknown()
    {
        var missing = JsonNode.Parse(ReadyObservation)!.AsObject();
        missing["SmbServer"]!.AsObject().Remove("RequireSecuritySignature");
        var forwardUnknown = JsonNode.Parse(ReadyObservation)!.AsObject();
        forwardUnknown["SmbServer"]!["Smb2DialectMax"] = "SMB400";

        var reports = new[]
        {
            await new WindowsSmbReadinessInspector(new StubJsonSource(missing.ToJsonString()))
                .InspectAsync(CancellationToken.None),
            await new WindowsSmbReadinessInspector(new StubJsonSource(forwardUnknown.ToJsonString()))
                .InspectAsync(CancellationToken.None),
            await new WindowsSmbReadinessInspector(
                    new StubJsonSource("{\"Credential\":\"do-not-leak\",\"broken\":"))
                .InspectAsync(CancellationToken.None),
        };

        Assert.IsTrue(reports.All(report => report.Status == CircleFilesReadinessStatus.Unknown));
        Assert.IsFalse(reports.Any(report =>
            report.Checks.Any(check => check.Summary.Contains("do-not-leak", StringComparison.Ordinal))));
    }

    [TestMethod]
    public async Task Command_failure_is_unknown_and_does_not_expose_the_underlying_error()
    {
        var inspector = new WindowsSmbReadinessInspector(
            new ThrowingJsonSource(new UnauthorizedAccessException("private machine details")));

        var report = await inspector.InspectAsync(CancellationToken.None);

        Assert.AreEqual(CircleFilesReadinessStatus.Unknown, report.Status);
        Assert.IsTrue(report.Checks.All(check => check.Code == "inspection_failed"));
        Assert.IsFalse(report.Checks.Any(check =>
            check.Summary.Contains("private machine details", StringComparison.Ordinal)));
    }

    private sealed class StubJsonSource(string json) : IWindowsPowerShellJsonSource
    {
        public List<WindowsPowerShellQuery> Queries { get; } = [];

        public ValueTask<string> QueryAsync(
            WindowsPowerShellQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Queries.Add(query);
            return ValueTask.FromResult(json);
        }
    }

    private sealed class ThrowingJsonSource(Exception exception) : IWindowsPowerShellJsonSource
    {
        public ValueTask<string> QueryAsync(
            WindowsPowerShellQuery query,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<string>(exception);
    }
}
