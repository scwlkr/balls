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
            Result(),
            Result(RunJson(package)),
            Result());
        var runner = new WindowsSmbReadinessConformanceRunner(processes, ReadGuestScript());
        var receiptPath = Path.Combine(receiptDirectory.Path, "receipt.json");

        var receipt = await runner.RunAsync(
            target,
            package,
            receiptPath,
            CancellationToken.None);

        Assert.AreEqual("passed", receipt.Outcome);
        Assert.AreEqual(Commit, receipt.Source.Commit);
        Assert.AreEqual("disposable-windows-lab", receipt.Target.TargetId);
        Assert.IsTrue(receipt.NativeStateUnchanged);
        Assert.IsTrue(receipt.Cleanup.Complete);
        Assert.HasCount(4, processes.Requests);
        Assert.AreEqual("ssh", processes.Requests[0].FileName);
        Assert.AreEqual("scp", processes.Requests[1].FileName);
        Assert.AreEqual("ssh", processes.Requests[2].FileName);
        Assert.AreEqual("ssh", processes.Requests[3].FileName);
        Assert.IsTrue(processes.Requests.All(request =>
            request.Arguments.Contains("StrictHostKeyChecking=yes")));
        Assert.IsTrue(processes.Requests.All(request =>
            request.Arguments.Contains("ClearAllForwardings=yes")));
        Assert.IsTrue(processes.Requests
            .Where(request => request.FileName == "ssh")
            .All(request => request.Arguments[^1].Length < 1000));
        Assert.IsTrue(processes.Requests
            .Where(request => request.FileName == "ssh")
            .All(request => request.StandardInput == ReadGuestScript()));
        Assert.IsNull(processes.Requests[1].StandardInput);
        Assert.IsFalse(processes.Requests[1].Arguments.Contains("-p"));
        var scpArguments = processes.Requests[1].Arguments.ToList();
        Assert.AreEqual(
            target.Transport.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
    public async Task Fixed_guest_failure_returns_only_its_whitelisted_code()
    {
        using var targetFixture = TargetProfileFixture.Create();
        using var packageFixture = PackageFixture.Create(Commit);
        using var receiptDirectory = TemporaryDirectory.Create();
        var processes = new FakeConformanceProcessRunner(
            Result(PreflightJson()),
            Result(),
            new ConformanceProcessResult(
                1,
                "{\"schema\":\"balls-windows-smb-readiness-guest-v1\",\"operation\":\"windows-smb-readiness-v1\",\"outcome\":\"failed\",\"code\":\"daemon_exited_status_C0000135\"}",
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

        Assert.AreEqual("guest_daemon_exited_status_C0000135", exception.Code);
        Assert.IsFalse(exception.Message.Contains("untrusted", StringComparison.Ordinal));
        Assert.HasCount(4, processes.Requests);
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
        Assert.HasCount(4, processes.Requests);
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

    private static ConformanceProcessResult Result(string output = "") => new(0, output, string.Empty);

    internal static string PreflightJson(string computerName = "BALLS-LAB") =>
        JsonSerializer.Serialize(new
        {
            schema = "balls-windows-smb-readiness-preflight-v1",
            operation = "windows-smb-readiness-v1",
            outcome = "ready",
            computerName,
            account = new { kind = "administrator", elevated = true, integrity = "high" },
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
            preflight = JsonSerializer.Deserialize<JsonElement>(PreflightJson()),
            product = new
            {
                commit = package.Commit,
                packageSha256 = package.Sha256,
                packageName = package.FileName,
                version = package.Version,
                cliVersion = package.Version,
                daemonVersion = package.Version,
            },
            productReadiness = new
            {
                provider = "windows-smb-3.1.1-v1",
                status = "ready",
                checks,
            },
            nativeObservation = new
            {
                serverSmb2Enabled = true,
                serverSigningRequired = true,
                serverEncryptionSupported = true,
                serverRejectsUnencryptedAccess = true,
                clientSigningRequired = true,
                clientEncryptionRequired = true,
                insecureGuestLogonsEnabled = false,
                serverSmb1FeatureState = "disabled",
                clientSmb1FeatureState = "disabled",
                networkCategories = new[] { "private" },
                firewallProfiles = new[] { "domain", "private", "public" },
                publicSmbAllowRules = 0,
                publicSmbBlockRules = 1,
            },
            nativeStateUnchanged = true,
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

    public Task<ConformanceProcessResult> RunAsync(
        ConformanceProcessRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var outcome = queued.Dequeue();
        return outcome switch
        {
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
