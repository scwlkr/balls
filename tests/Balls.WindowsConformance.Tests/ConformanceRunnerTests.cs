using System.Text.Json;
using Balls.WindowsConformance;

namespace Balls.WindowsConformance.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class ConformanceRunnerTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [TestMethod]
    public async Task Fixed_transport_returns_one_exact_redacted_receipt()
    {
        using var targetFixture = TargetProfileFixture.Create();
        using var packageFixture = PackageFixture.Create(Commit);
        using var receiptDirectory = TemporaryDirectory.Create();
        var target = WindowsConformanceTargetProfileLoader.Load(targetFixture.Path);
        var package = WindowsPackageIdentityLoader.Load(
            packageFixture.PackagePath,
            packageFixture.ChecksumPath,
            Commit);
        var processes = new FakeConformanceProcessRunner(
            Result(PreflightJson()),
            Result(PreflightJson(productAccount: true)),
            Result(NativeJson()),
            Result(),
            Result(RunJson(package)),
            Result(),
            Result(NativeJson()));
        var runner = new WindowsSmbReadinessConformanceRunner(processes, ReadGuestScript());
        var receiptPath = Path.Combine(receiptDirectory.Path, "receipt.json");

        var receipt = await runner.RunAsync(
            target,
            package,
            receiptPath,
            CancellationToken.None);

        Assert.AreEqual("passed", receipt.Outcome);
        Assert.AreEqual(Commit, receipt.Source.Commit);
        Assert.AreEqual("unelevated", receipt.Source.DaemonPrivilege);
        Assert.AreEqual("disposable-windows-lab", receipt.Target.TargetId);
        Assert.IsTrue(receipt.NativeStateUnchanged);
        Assert.IsTrue(receipt.Cleanup.Complete);
        Assert.HasCount(7, processes.Requests);
        Assert.AreEqual("ssh", processes.Requests[0].FileName);
        Assert.AreEqual("ssh", processes.Requests[1].FileName);
        Assert.AreEqual("ssh", processes.Requests[2].FileName);
        Assert.AreEqual("scp", processes.Requests[3].FileName);
        Assert.AreEqual("ssh", processes.Requests[4].FileName);
        Assert.AreEqual("ssh", processes.Requests[5].FileName);
        Assert.AreEqual("ssh", processes.Requests[6].FileName);
        Assert.IsTrue(processes.Requests.All(request =>
            request.Arguments.Contains("StrictHostKeyChecking=yes")));
        Assert.IsTrue(processes.Requests.All(request =>
            request.Arguments.Contains("ClearAllForwardings=yes")));
        Assert.IsTrue(processes.Requests
            .Where(request => request.FileName == "ssh")
            .All(request => request.Arguments[^1].Length < 1000));
        Assert.IsTrue(processes.Requests
            .Where(request => request.FileName == "ssh")
            .All(request => request.StandardInput!.StartsWith(
                    ReadGuestScript().TrimEnd('\r', '\n'),
                    StringComparison.Ordinal)
                && request.StandardInput.EndsWith(
                    "__BALLS_CONFORMANCE_OPERATION_END__" + Environment.NewLine,
                    StringComparison.Ordinal)));
        Assert.IsTrue(processes.Requests
            .Where(request => request.FileName == "ssh")
            .All(request => request.Arguments[^1].Contains(
                "[Console]::In.ReadLine()",
                StringComparison.Ordinal)));
        Assert.IsNull(processes.Requests[3].StandardInput);
        Assert.IsFalse(processes.Requests[3].Arguments.Contains("-p"));
        var scpArguments = processes.Requests[3].Arguments.ToList();
        Assert.AreEqual(
            target.ProductTransport.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            scpArguments[scpArguments.IndexOf("-P") + 1]);
        var serialized = File.ReadAllText(receiptPath);
        StringAssert.Contains(serialized, "windows-smb-3.1.1-v1");
        Assert.IsFalse(serialized.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("privateKey", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Target_identity_mismatch_refuses_before_package_transfer()
    {
        using var targetFixture = TargetProfileFixture.Create();
        using var packageFixture = PackageFixture.Create(Commit);
        using var receiptDirectory = TemporaryDirectory.Create();
        var processes = new FakeConformanceProcessRunner(Result(PreflightJson("OTHER-LAB")));
        var runner = new WindowsSmbReadinessConformanceRunner(processes, "Write-Output fixed");

        var exception = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            runner.RunAsync(
                WindowsConformanceTargetProfileLoader.Load(targetFixture.Path),
                WindowsPackageIdentityLoader.Load(
                    packageFixture.PackagePath,
                    packageFixture.ChecksumPath,
                    Commit),
                Path.Combine(receiptDirectory.Path, "receipt.json"),
                CancellationToken.None));

        Assert.AreEqual("target_identity_or_precondition_mismatch", exception.Code);
        Assert.HasCount(1, processes.Requests);
    }

    [TestMethod]
    public async Task Secret_bearing_preflight_is_rejected_without_echoing_it()
    {
        using var targetFixture = TargetProfileFixture.Create();
        using var packageFixture = PackageFixture.Create(Commit);
        using var receiptDirectory = TemporaryDirectory.Create();
        var preflight = PreflightJson()[..^1] + ",\"password\":\"must-not-escape\"}";
        var processes = new FakeConformanceProcessRunner(Result(preflight));
        var runner = new WindowsSmbReadinessConformanceRunner(processes, "Write-Output fixed");

        var exception = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            runner.RunAsync(
                WindowsConformanceTargetProfileLoader.Load(targetFixture.Path),
                WindowsPackageIdentityLoader.Load(
                    packageFixture.PackagePath,
                    packageFixture.ChecksumPath,
                    Commit),
                Path.Combine(receiptDirectory.Path, "receipt.json"),
                CancellationToken.None));

        Assert.AreEqual("receipt_contains_secret", exception.Code);
        Assert.IsFalse(exception.Message.Contains("must-not-escape", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Malformed_or_oversized_preflight_fails_closed()
    {
        foreach (var value in new[] { "not-json", new string(' ', 70 * 1024) })
        {
            using var targetFixture = TargetProfileFixture.Create();
            using var packageFixture = PackageFixture.Create(Commit);
            using var receiptDirectory = TemporaryDirectory.Create();
            var runner = new WindowsSmbReadinessConformanceRunner(
                new FakeConformanceProcessRunner(Result(value)),
                "Write-Output fixed");

            var exception = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
                runner.RunAsync(
                    WindowsConformanceTargetProfileLoader.Load(targetFixture.Path),
                    WindowsPackageIdentityLoader.Load(
                        packageFixture.PackagePath,
                        packageFixture.ChecksumPath,
                        Commit),
                    Path.Combine(receiptDirectory.Path, "receipt.json"),
                    CancellationToken.None));

            Assert.AreEqual("preflight_invalid", exception.Code);
        }
    }

    [TestMethod]
    public async Task Structurally_incomplete_preflight_fails_with_the_stable_invalid_code()
    {
        using var targetFixture = TargetProfileFixture.Create();
        using var packageFixture = PackageFixture.Create(Commit);
        using var receiptDirectory = TemporaryDirectory.Create();
        var runner = new WindowsSmbReadinessConformanceRunner(
            new FakeConformanceProcessRunner(Result("{}")),
            "Write-Output fixed");

        var exception = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            runner.RunAsync(
                WindowsConformanceTargetProfileLoader.Load(targetFixture.Path),
                WindowsPackageIdentityLoader.Load(
                    packageFixture.PackagePath,
                    packageFixture.ChecksumPath,
                    Commit),
                Path.Combine(receiptDirectory.Path, "receipt.json"),
                CancellationToken.None));

        Assert.AreEqual("preflight_invalid", exception.Code);
    }

    [TestMethod]
    public async Task Timeout_is_a_stable_failure_without_raw_transport_output()
    {
        using var targetFixture = TargetProfileFixture.Create();
        using var packageFixture = PackageFixture.Create(Commit);
        using var receiptDirectory = TemporaryDirectory.Create();
        var processes = new FakeConformanceProcessRunner(
            new ConformanceRefusalException("transport_timeout"));
        var runner = new WindowsSmbReadinessConformanceRunner(processes, "Write-Output fixed");

        var exception = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            runner.RunAsync(
                WindowsConformanceTargetProfileLoader.Load(targetFixture.Path),
                WindowsPackageIdentityLoader.Load(
                    packageFixture.PackagePath,
                    packageFixture.ChecksumPath,
                    Commit),
                Path.Combine(receiptDirectory.Path, "receipt.json"),
                CancellationToken.None));

        Assert.AreEqual("transport_timeout", exception.Code);
    }

    [TestMethod]
    public async Task Cancellation_after_transfer_still_attempts_cleanup_with_its_own_bound()
    {
        using var targetFixture = TargetProfileFixture.Create();
        using var packageFixture = PackageFixture.Create(Commit);
        using var receiptDirectory = TemporaryDirectory.Create();
        using var cancellation = new CancellationTokenSource();
        var processes = new FakeConformanceProcessRunner(
            Result(PreflightJson()),
            Result(PreflightJson(productAccount: true)),
            Result(NativeJson()),
            Result(),
            (Func<CancellationToken, Task<ConformanceProcessResult>>)(token =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<ConformanceProcessResult>(token);
            }),
            Result());
        var runner = new WindowsSmbReadinessConformanceRunner(processes, "Write-Output fixed");

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
            runner.RunAsync(
                WindowsConformanceTargetProfileLoader.Load(targetFixture.Path),
                WindowsPackageIdentityLoader.Load(
                    packageFixture.PackagePath,
                    packageFixture.ChecksumPath,
                    Commit),
                Path.Combine(receiptDirectory.Path, "receipt.json"),
                cancellation.Token));

        Assert.HasCount(6, processes.Requests);
        Assert.IsFalse(processes.CancellationTokens[5].IsCancellationRequested);
        Assert.AreEqual(TimeSpan.FromSeconds(60), processes.Requests[5].Timeout);
    }

    [TestMethod]
    public async Task Fixed_guest_failure_returns_only_its_whitelisted_code()
    {
        using var targetFixture = TargetProfileFixture.Create();
        using var packageFixture = PackageFixture.Create(Commit);
        using var receiptDirectory = TemporaryDirectory.Create();
        var processes = new FakeConformanceProcessRunner(
            Result(PreflightJson()),
            Result(PreflightJson(productAccount: true)),
            Result(NativeJson()),
            Result(),
            new ConformanceProcessResult(
                1,
                "{\"schema\":\"balls-windows-smb-readiness-guest-v1\",\"operation\":\"windows-smb-readiness-v1\",\"outcome\":\"failed\",\"code\":\"guest_operation_unhandled_daemon_poll\"}",
                "untrusted raw detail"),
            Result());
        var runner = new WindowsSmbReadinessConformanceRunner(processes, "Write-Output fixed");

        var exception = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            runner.RunAsync(
                WindowsConformanceTargetProfileLoader.Load(targetFixture.Path),
                WindowsPackageIdentityLoader.Load(
                    packageFixture.PackagePath,
                    packageFixture.ChecksumPath,
                    Commit),
                Path.Combine(receiptDirectory.Path, "receipt.json"),
                CancellationToken.None));

        Assert.AreEqual("guest_guest_operation_unhandled_daemon_poll", exception.Code);
        Assert.IsFalse(exception.Message.Contains("untrusted", StringComparison.Ordinal));
        Assert.HasCount(6, processes.Requests);
    }

    [TestMethod]
    public async Task Empty_guest_failure_output_returns_only_a_stable_shape_code()
    {
        using var targetFixture = TargetProfileFixture.Create();
        using var packageFixture = PackageFixture.Create(Commit);
        using var receiptDirectory = TemporaryDirectory.Create();
        var processes = new FakeConformanceProcessRunner(
            Result(PreflightJson()),
            Result(PreflightJson(productAccount: true)),
            Result(NativeJson()),
            Result(),
            new ConformanceProcessResult(1, string.Empty, "discarded raw error"),
            Result());
        var runner = new WindowsSmbReadinessConformanceRunner(processes, "Write-Output fixed");

        var exception = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            runner.RunAsync(
                WindowsConformanceTargetProfileLoader.Load(targetFixture.Path),
                WindowsPackageIdentityLoader.Load(
                    packageFixture.PackagePath,
                    packageFixture.ChecksumPath,
                    Commit),
                Path.Combine(receiptDirectory.Path, "receipt.json"),
                CancellationToken.None));

        Assert.AreEqual("guest_failure_output_empty", exception.Code);
        Assert.IsFalse(exception.Message.Contains("discarded", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("native_inspection_firewall_rules_failed", "native_inspection_firewall_rules_failed")]
    [DataRow("native_inspection_private_identifier_failed", "native_inspection_failed")]
    public async Task Native_failure_output_is_limited_to_approved_shape_codes(
        string code,
        string expected)
    {
        using var targetFixture = TargetProfileFixture.Create();
        using var packageFixture = PackageFixture.Create(Commit);
        using var receiptDirectory = TemporaryDirectory.Create();
        var runner = new WindowsSmbReadinessConformanceRunner(
            new FakeConformanceProcessRunner(
                Result(PreflightJson()),
                Result(PreflightJson(productAccount: true)),
                new ConformanceProcessResult(1, NativeFailureJson(code), "discarded raw error")),
            "Write-Output fixed");

        var exception = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            runner.RunAsync(
                WindowsConformanceTargetProfileLoader.Load(targetFixture.Path),
                WindowsPackageIdentityLoader.Load(
                    packageFixture.PackagePath,
                    packageFixture.ChecksumPath,
                    Commit),
                Path.Combine(receiptDirectory.Path, "receipt.json"),
                CancellationToken.None));

        Assert.AreEqual(expected, exception.Code);
        Assert.IsFalse(exception.Message.Contains("discarded", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Incomplete_guest_cleanup_is_not_a_pass()
    {
        using var targetFixture = TargetProfileFixture.Create();
        using var packageFixture = PackageFixture.Create(Commit);
        using var receiptDirectory = TemporaryDirectory.Create();
        var target = WindowsConformanceTargetProfileLoader.Load(targetFixture.Path);
        var package = WindowsPackageIdentityLoader.Load(
            packageFixture.PackagePath,
            packageFixture.ChecksumPath,
            Commit);
        var run = RunJson(package).Replace(
            "\"complete\":true",
            "\"complete\":false",
            StringComparison.Ordinal);
        var processes = new FakeConformanceProcessRunner(
            Result(PreflightJson()),
            Result(PreflightJson(productAccount: true)),
            Result(NativeJson()),
            Result(),
            Result(run),
            Result());
        var runner = new WindowsSmbReadinessConformanceRunner(processes, "Write-Output fixed");

        var exception = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            runner.RunAsync(
                target,
                package,
                Path.Combine(receiptDirectory.Path, "receipt.json"),
                CancellationToken.None));

        Assert.AreEqual("result_identity_or_contract_mismatch", exception.Code);
        Assert.HasCount(6, processes.Requests);
    }

    [TestMethod]
    public async Task Duplicated_readiness_check_identity_is_not_a_pass()
    {
        using var targetFixture = TargetProfileFixture.Create();
        using var packageFixture = PackageFixture.Create(Commit);
        using var receiptDirectory = TemporaryDirectory.Create();
        var target = WindowsConformanceTargetProfileLoader.Load(targetFixture.Path);
        var package = WindowsPackageIdentityLoader.Load(
            packageFixture.PackagePath,
            packageFixture.ChecksumPath,
            Commit);
        var run = RunJson(package).Replace(
            "\"id\":\"firewall-scope\"",
            "\"id\":\"windows-platform\"",
            StringComparison.Ordinal);
        var processes = new FakeConformanceProcessRunner(
            Result(PreflightJson()),
            Result(PreflightJson(productAccount: true)),
            Result(NativeJson()),
            Result(),
            Result(run),
            Result());
        var runner = new WindowsSmbReadinessConformanceRunner(processes, "Write-Output fixed");

        var exception = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            runner.RunAsync(
                target,
                package,
                Path.Combine(receiptDirectory.Path, "receipt.json"),
                CancellationToken.None));

        Assert.AreEqual("result_identity_or_contract_mismatch", exception.Code);
    }

    [TestMethod]
    [DataRow("server-service")]
    [DataRow("dialect")]
    [DataRow("smb1")]
    [DataRow("cipher")]
    [DataRow("private-network")]
    [DataRow("firewall-service")]
    [DataRow("private-default-inbound")]
    [DataRow("public-default-inbound")]
    [DataRow("public-smb-allow")]
    public async Task Ready_product_result_cannot_contradict_independent_native_evidence(
        string contradiction)
    {
        using var targetFixture = TargetProfileFixture.Create();
        using var packageFixture = PackageFixture.Create(Commit);
        using var receiptDirectory = TemporaryDirectory.Create();
        var target = WindowsConformanceTargetProfileLoader.Load(targetFixture.Path);
        var package = WindowsPackageIdentityLoader.Load(
            packageFixture.PackagePath,
            packageFixture.ChecksumPath,
            Commit);
        var run = RunJson(package);
        var native = contradiction switch
        {
            "server-service" => NativeJson(serverServiceRunning: false),
            "dialect" => NativeJson(serverMaximumDialect: "SMB302"),
            "smb1" => NativeJson(serverSmb1Enabled: true),
            "cipher" => NativeJson(serverEncryptionCiphers: ["AES_128_CCM"]),
            "private-network" => NativeJson(connectedPrivateProfiles: 0),
            "firewall-service" => NativeJson(firewallServiceRunning: false),
            "private-default-inbound" => NativeJson(privateDefaultInboundAction: "Allow"),
            "public-default-inbound" => NativeJson(publicDefaultInboundAction: "Allow"),
            "public-smb-allow" => NativeJson(publicSmbAllowRules: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(contradiction)),
        };
        var runner = new WindowsSmbReadinessConformanceRunner(
            new FakeConformanceProcessRunner(
                Result(PreflightJson()),
                Result(PreflightJson(productAccount: true)),
                Result(native),
                Result(),
                Result(run),
                Result(),
                Result(native)),
            "Write-Output fixed");

        var exception = await Assert.ThrowsExactlyAsync<ConformanceRefusalException>(() =>
            runner.RunAsync(
                target,
                package,
                Path.Combine(receiptDirectory.Path, "receipt.json"),
                CancellationToken.None));

        Assert.AreEqual("native_corroboration_mismatch", exception.Code);
    }

    [TestMethod]
    public async Task Extra_native_posture_does_not_overconstrain_the_product_contract()
    {
        using var targetFixture = TargetProfileFixture.Create();
        using var packageFixture = PackageFixture.Create(Commit);
        using var receiptDirectory = TemporaryDirectory.Create();
        var target = WindowsConformanceTargetProfileLoader.Load(targetFixture.Path);
        var package = WindowsPackageIdentityLoader.Load(
            packageFixture.PackagePath,
            packageFixture.ChecksumPath,
            Commit);
        var native = NativeJson(
            insecureGuestLogonsEnabled: true,
            serverSmb1FeatureState: "enabled");
        var runner = new WindowsSmbReadinessConformanceRunner(
            new FakeConformanceProcessRunner(
                Result(PreflightJson()),
                Result(PreflightJson(productAccount: true)),
                Result(native),
                Result(),
                Result(RunJson(package)),
                Result(),
                Result(native)),
            "Write-Output fixed");

        await runner.RunAsync(
            target,
            package,
            Path.Combine(receiptDirectory.Path, "receipt.json"),
            CancellationToken.None);
    }

    private static ConformanceProcessResult Result(string output = "") => new(0, output, string.Empty);

    internal static string PreflightJson(
        string computerName = "BALLS-LAB",
        bool productAccount = false) =>
        JsonSerializer.Serialize(new
        {
            schema = "balls-windows-smb-readiness-preflight-v1",
            operation = "windows-smb-readiness-v1",
            outcome = "ready",
            computerName,
            account = productAccount
                ? new
                {
                    kind = "standard",
                    elevated = false,
                    integrity = "medium",
                    identitySha256 = new string('a', 64),
                }
                : new
                {
                    kind = "administrator",
                    elevated = true,
                    integrity = "high",
                    identitySha256 = new string('b', 64),
                },
            windows = new
            {
                productName = "Windows Server 2025",
                displayVersion = "24H2",
                buildNumber = "26100",
                installationType = "Server",
            },
            policy = new
            {
                executionPolicy = "Restricted",
                uacEnabled = true,
                applicationControl = "off",
            },
            network = new
            {
                categories = new[] { "private" },
                firewallProfiles = new[] { "domain", "private", "public" },
            },
            dirtyState = new { existingBallsProcesses = 0, ownedArtifacts = 0, clean = true },
        });

    internal static string RunJson(WindowsPackageIdentity package)
    {
        var checkIds = new[]
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
        };
        var checks = checkIds.Select((id, index) => new
        {
            id,
            status = "ready",
            code = "observed_safe",
            summary = $"Readiness check {index + 1} passed.",
        });
        return JsonSerializer.Serialize(new
        {
            schema = "balls-windows-smb-readiness-guest-v1",
            operation = "windows-smb-readiness-v1",
            outcome = "passed",
            preflight = JsonSerializer.Deserialize<JsonElement>(PreflightJson(productAccount: true)),
            product = new
            {
                commit = package.Commit,
                packageSha256 = package.Sha256,
                packageName = package.FileName,
                version = package.Version,
                cliVersion = package.Version,
                daemonVersion = package.Version,
                daemonPrivilege = "unelevated",
            },
            productReadiness = new
            {
                provider = "windows-smb-3.1.1-v1",
                status = "ready",
                checks,
            },
            cleanup = new
            {
                daemonStopped = true,
                stateRemoved = true,
                packageRemoved = true,
                complete = true,
            },
            limitations = new[]
            {
                "read-only Windows conformance; no operating-system mutation",
                "not GUI, UAC, Explorer, physical-device, or release acceptance",
            },
        });
    }

    internal static string NativeJson(
        bool serverServiceRunning = true,
        bool firewallServiceRunning = true,
        bool serverSmb1Enabled = false,
        bool serverSmb2Enabled = true,
        string serverMaximumDialect = "SMB311",
        string[]? serverEncryptionCiphers = null,
        int connectedPrivateProfiles = 1,
        string privateDefaultInboundAction = "Block",
        string publicDefaultInboundAction = "Block",
        bool insecureGuestLogonsEnabled = false,
        string serverSmb1FeatureState = "disabled",
        int publicSmbAllowRules = 0) =>
        JsonSerializer.Serialize(new
        {
            schema = "balls-windows-smb-readiness-native-v1",
            operation = "windows-smb-readiness-v1",
            outcome = "observed",
            observation = new
            {
                serverServiceRunning,
                firewallServiceRunning,
                serverSmb1Enabled,
                serverSmb2Enabled,
                serverMaximumDialect,
                serverSigningRequired = true,
                serverEncryptionSupported = true,
                serverRejectsUnencryptedAccess = true,
                serverEncryptionCiphers = serverEncryptionCiphers ?? ["AES_128_GCM"],
                clientSigningRequired = true,
                clientEncryptionRequired = true,
                insecureGuestLogonsEnabled,
                serverSmb1FeatureState,
                clientSmb1FeatureState = "disabled",
                connectedPrivateProfiles,
                networkCategories = new[] { "private" },
                privateFirewallEnabled = true,
                privateDefaultInboundAction,
                publicFirewallEnabled = true,
                publicDefaultInboundAction,
                firewallProfiles = new[] { "domain", "private", "public" },
                publicSmbAllowRules,
                publicSmbBlockRules = 1,
            },
        });

    private static string NativeFailureJson(string code) =>
        JsonSerializer.Serialize(new
        {
            schema = "balls-windows-smb-readiness-native-v1",
            operation = "windows-smb-readiness-v1",
            outcome = "failed",
            code,
        });

    private static string ReadGuestScript()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(
                directory.FullName,
                "eng",
                "conformance",
                "Invoke-WindowsSmbReadinessConformance.ps1");
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Guest conformance operation not found.");
    }
}

internal sealed class FakeConformanceProcessRunner(params object[] outcomes) : IConformanceProcessRunner
{
    private readonly Queue<object> queued = new(outcomes);

    public List<ConformanceProcessRequest> Requests { get; } = [];

    public List<CancellationToken> CancellationTokens { get; } = [];

    public Task<ConformanceProcessResult> RunAsync(
        ConformanceProcessRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        CancellationTokens.Add(cancellationToken);
        var outcome = queued.Dequeue();
        return outcome switch
        {
            Func<CancellationToken, Task<ConformanceProcessResult>> operation =>
                operation(cancellationToken),
            Exception exception => Task.FromException<ConformanceProcessResult>(exception),
            ConformanceProcessResult result => Task.FromResult(result),
            _ => throw new InvalidOperationException("Unknown fake process outcome."),
        };
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    private TemporaryDirectory(string path) => Path = path;

    public string Path { get; }

    public static TemporaryDirectory Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"balls-conformance-receipt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TemporaryDirectory(path);
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
