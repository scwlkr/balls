using System.Text.RegularExpressions;

namespace Balls.Verify.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed partial class RepositoryWorkflowTests
{
    private static readonly string Workflow = File.ReadAllText(
        Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ci.yml"));

    [TestMethod]
    public void Required_lanes_use_fixed_images_and_stable_names()
    {
        Assert.IsFalse(Workflow.Contains("-latest", StringComparison.Ordinal));
        StringAssert.Contains(Workflow, "windows-fast:");
        StringAssert.Contains(Workflow, "name: Windows fast");
        StringAssert.Contains(Workflow, "runs-on: windows-2025");
        StringAssert.Contains(Workflow, "ubuntu-fast:");
        StringAssert.Contains(Workflow, "name: Ubuntu fast");
        StringAssert.Contains(Workflow, "runs-on: ubuntu-24.04");
        StringAssert.Contains(Workflow, "macos-fast:");
        StringAssert.Contains(Workflow, "name: macOS fast");
        StringAssert.Contains(Workflow, "runs-on: macos-26");
    }

    [TestMethod]
    public void Aggregate_is_fail_closed_over_all_platform_lanes()
    {
        StringAssert.Contains(Workflow, "required:");
        StringAssert.Contains(Workflow, "name: Required");
        StringAssert.Contains(Workflow, "needs: [windows-fast, ubuntu-fast, macos-fast]");
        StringAssert.Contains(Workflow, "if: ${{ always() }}");
        StringAssert.Contains(Workflow, "WINDOWS_RESULT: ${{ needs.windows-fast.result }}");
        StringAssert.Contains(Workflow, "UBUNTU_RESULT: ${{ needs.ubuntu-fast.result }}");
        StringAssert.Contains(Workflow, "MACOS_RESULT: ${{ needs.macos-fast.result }}");
    }

    [TestMethod]
    public void Build_lanes_install_the_pinned_web_toolchain_and_fast_lanes_cache_pnpm()
    {
        Assert.AreEqual(5, Regex.Matches(Workflow, "node-version-file: .node-version").Count);
        Assert.AreEqual(5, Regex.Matches(Workflow, "name: Enable pinned pnpm").Count);
        Assert.AreEqual(2, Regex.Matches(Workflow, "name: Install browser dependencies").Count);
        Assert.AreEqual(2, Regex.Matches(Workflow, "pnpm install --frozen-lockfile").Count);
        Assert.AreEqual(3, Regex.Matches(Workflow, "name: Cache pnpm store").Count);
        Assert.AreEqual(3, Regex.Matches(Workflow, "hashFiles\\('pnpm-lock.yaml'\\)").Count);
        StringAssert.Contains(
            Workflow,
            "actions/setup-node@2028fbc5c25fe9cf00d9f06a71cc4710d4507903");
        StringAssert.Contains(
            Workflow,
            "actions/cache@0057852bfaa89a56745cba8c7296529d2fc39830");
    }

    [TestMethod]
    public void Browser_workspace_is_pinned_and_keeps_protocol_dtos_at_the_api_edge()
    {
        var root = FindRepositoryRoot();
        var rootPackage = File.ReadAllText(Path.Combine(root, "package.json"));
        var gitAttributes = File.ReadAllText(Path.Combine(root, ".gitattributes"));
        var apiDirectory = Path.Combine(root, "web", "Balls.Web", "src", "api");
        var componentDirectory = Path.Combine(root, "web", "Balls.Web", "src", "components");

        Assert.AreEqual("24.18.0", File.ReadAllText(Path.Combine(root, ".node-version")).Trim());
        StringAssert.Contains(rootPackage, "\"packageManager\": \"pnpm@11.19.0\"");
        StringAssert.Contains(gitAttributes, "*.ts text eol=lf");
        StringAssert.Contains(gitAttributes, "*.tsx text eol=lf");
        Assert.IsTrue(File.Exists(Path.Combine(root, "pnpm-lock.yaml")));
        Assert.IsTrue(File.Exists(Path.Combine(
            root,
            "docs",
            "protocol",
            "local-control-v1.openapi.json")));
        StringAssert.Contains(
            File.ReadAllText(Path.Combine(apiDirectory, "localControl.ts")),
            "openapi-fetch");
        StringAssert.Contains(
            File.ReadAllText(Path.Combine(apiDirectory, "demoSnapshot.ts")),
            "satisfies CircleDetailsDto");

        var componentSource = string.Join(
            Environment.NewLine,
            Directory.GetFiles(componentDirectory, "*.tsx").Select(File.ReadAllText));
        Assert.IsFalse(componentSource.Contains("api/generated", StringComparison.Ordinal));
        Assert.IsFalse(componentSource.Contains("localControl", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Workflow_preserves_supply_chain_and_concurrency_guards()
    {
        StringAssert.Contains(Workflow, "permissions:");
        StringAssert.Contains(Workflow, "contents: read");
        StringAssert.Contains(Workflow, "cancel-in-progress: ${{ github.event_name == 'pull_request' }}");
        StringAssert.Contains(Workflow, "cache: true");

        var usesLines = Workflow.Split('\n')
            .Where(line => line.TrimStart().StartsWith("uses:", StringComparison.Ordinal))
            .ToArray();
        Assert.IsNotEmpty(usesLines);
        Assert.IsTrue(usesLines.All(line => ShaPinnedAction().IsMatch(line)));
    }

    [TestMethod]
    public void Canary_publication_follows_successful_required_push_or_dispatch()
    {
        StringAssert.Contains(Workflow, "windows-canary:");
        StringAssert.Contains(Workflow, "linux-canary:");
        StringAssert.Contains(Workflow, "Smoke packaged Windows Canary");
        StringAssert.Contains(Workflow, "Smoke packaged Linux Canary");
        StringAssert.Contains(Workflow, "Test-LinuxCanary.sh");
        StringAssert.Contains(Workflow, "Install-BallsCanary.sh");
        Assert.AreEqual(2, Regex.Matches(Workflow, @"(?m)^    needs: required$").Count);
        Assert.AreEqual(2, Regex.Matches(Workflow, @"github.event_name == 'push'").Count);
        Assert.AreEqual(2, Regex.Matches(Workflow, @"github.event_name == 'workflow_dispatch'").Count);
        Assert.AreEqual(2, Regex.Matches(Workflow, @"github.ref == 'refs/heads/main'").Count);
        Assert.AreEqual(2, Regex.Matches(Workflow, @"needs.required.result == 'success'").Count);

        var repositoryRoot = FindRepositoryRoot();
        var windowsSmoke = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "canary", "Test-WindowsCanary.ps1"));
        var linuxSmoke = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "canary", "Test-LinuxCanary.sh"));
        var linuxInstaller = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "canary", "Install-BallsCanary.sh"));
        foreach (var smoke in new[] { windowsSmoke, linuxSmoke })
        {
            StringAssert.Contains(smoke, "Canary Circle");
            StringAssert.Contains(smoke, "browser");
            StringAssert.Contains(smoke, "status");
        }
        StringAssert.Contains(windowsSmoke, "'circle', 'create'");
        StringAssert.Contains(windowsSmoke, "'circle', 'list'");
        StringAssert.Contains(linuxSmoke, "circle create");
        StringAssert.Contains(linuxSmoke, "circle list");
        StringAssert.Contains(linuxSmoke, "awk '{print $4}'");
        StringAssert.Contains(linuxSmoke, "127\\\\.0\\\\.0\\\\.1");
        StringAssert.Contains(linuxInstaller, "$HOME/.balls-canary");
        StringAssert.Contains(linuxInstaller, "runtime_root=\"$install_root/runtime\"");
        Assert.IsFalse(linuxInstaller.Contains("/tmp/balls-canary", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Lab_harness_namespaces_resources_and_gates_identity_reset_and_cleanup()
    {
        var harness = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "eng", "lab", "Invoke-BallsLab.ps1"));

        StringAssert.Contains(harness, "Balls.Lab.Ubuntu");
        StringAssert.Contains(harness, "Balls.Lab.Clean");
        StringAssert.Contains(harness, "PrepareImage");
        StringAssert.Contains(harness, "Assert-CleanIdentity");
        StringAssert.Contains(harness, "ConfirmReset");
        StringAssert.Contains(harness, "ConfirmCleanup");
        StringAssert.Contains(harness, "cloud-images.ubuntu.com");
        StringAssert.Contains(harness, "Refusing to adopt");
        StringAssert.Contains(harness, "QemuImgWslPath");
        StringAssert.Contains(harness, "subformat=dynamic");
        StringAssert.Contains(harness, "$minimumOsDiskSize = 16GB");
        StringAssert.Contains(harness, "Resize-VHD -Path $baseVhdPath");
        StringAssert.Contains(harness, "Generation = 2");
        StringAssert.Contains(harness, "EnableSecureBoot Off");
        StringAssert.Contains(harness, "AutomaticCheckpointsEnabled $false");
        StringAssert.Contains(harness, "aspnetcore-runtime-10.0");
        StringAssert.Contains(harness, "  - unzip");
        StringAssert.Contains(harness, "$HOME/.local/share/Balls-Canary");
        StringAssert.Contains(harness, "$HOME/.balls-canary");
        StringAssert.Contains(harness, "within 30 minutes");
    }

    [TestMethod]
    public void Two_machine_development_link_is_private_key_only_and_confirmation_gated()
    {
        var root = FindRepositoryRoot();
        var windowsBootstrap = File.ReadAllText(
            Path.Combine(root, "eng", "remote", "Initialize-BallsDevLink.ps1"));
        var macBootstrap = File.ReadAllText(
            Path.Combine(root, "eng", "remote", "Initialize-BallsDevLink.sh"));
        var runbook = File.ReadAllText(
            Path.Combine(root, "docs", "two-machine-development.md"));

        StringAssert.Contains(windowsBootstrap, "ValidateSet('Inspect', 'Configure', 'Finalize', 'Disable')");
        StringAssert.Contains(windowsBootstrap, "ConfirmSystemChange");
        StringAssert.Contains(windowsBootstrap, "100.64.0.0/10");
        StringAssert.Contains(windowsBootstrap, "Tailscale");
        StringAssert.Contains(windowsBootstrap, "Set-SshdGlobalOption -Name 'PasswordAuthentication' -Value 'no'");
        StringAssert.Contains(windowsBootstrap, "Set-SshdGlobalOption -Name 'PubkeyAuthentication' -Value 'yes'");
        Assert.IsFalse(windowsBootstrap.Contains("Invoke-Expression", StringComparison.Ordinal));
        Assert.IsFalse(windowsBootstrap.Contains("Invoke-WebRequest", StringComparison.Ordinal));

        StringAssert.Contains(macBootstrap, "inspect|configure|disable");
        StringAssert.Contains(macBootstrap, "--confirm-system-change");
        StringAssert.Contains(macBootstrap, "set --ssh=true");
        Assert.IsFalse(macBootstrap.Contains("systemsetup -setremotelogin on", StringComparison.Ordinal));
        Assert.IsFalse(macBootstrap.Contains("curl ", StringComparison.Ordinal));

        StringAssert.Contains(runbook, "GitHub remains the source of truth");
        StringAssert.Contains(runbook, "1Password SSH agent");
        StringAssert.Contains(runbook, "No passwords, private keys, or repository files cross through GitHub Issues");
        StringAssert.Contains(runbook, "physical Mac-to-Windows");
    }

    [TestMethod]
    public void Security_workflows_exist_and_keep_untrusted_pull_requests_unprivileged()
    {
        var workflowDirectory = Path.Combine(FindRepositoryRoot(), ".github", "workflows");
        var requiredFiles = new[] { "dependency-review.yml", "codeql.yml", "scorecard.yml" };
        foreach (var file in requiredFiles)
        {
            Assert.IsTrue(File.Exists(Path.Combine(workflowDirectory, file)), file);
        }

        var workflows = Directory.GetFiles(workflowDirectory, "*.yml")
            .Select(File.ReadAllText)
            .ToArray();
        var combined = string.Join(Environment.NewLine, workflows);
        Assert.IsFalse(combined.Contains("pull_request_target", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("workflow_run", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("self-hosted", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("secrets.", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("-latest", StringComparison.Ordinal));

        var usesLines = workflows
            .SelectMany(workflow => workflow.Split('\n'))
            .Where(line => line.TrimStart().StartsWith("uses:", StringComparison.Ordinal))
            .ToArray();
        Assert.IsNotEmpty(usesLines);
        Assert.IsTrue(usesLines.All(line => ShaPinnedAction().IsMatch(line)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Balls.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Balls.slnx from the test directory.");
    }

    [GeneratedRegex(@"^\s*uses:\s+[^@\s]+@[0-9a-f]{40}(?:\s+#.*)?$", RegexOptions.Multiline)]
    private static partial Regex ShaPinnedAction();
}
