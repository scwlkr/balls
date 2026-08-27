using Balls.Platform;
using Balls.Platform.Windows;
using System.Runtime.Versioning;

namespace Balls.Platform.Windows.Tests;

[TestClass]
[TestCategory("Unit")]
[SupportedOSPlatform("windows")]
public sealed class WindowsRevitServerSetupTests
{
    [TestMethod]
    public void Elevated_preparation_is_closed_to_the_exact_documented_surface()
    {
        var script = WindowsRevitServerSystemOperations.Script;
        foreach (var feature in new[]
        {
            "Web-Server",
            "Web-Asp-Net45",
            "NET-WCF-HTTP-Activation45",
            "NET-WCF-TCP-Activation45",
            "Web-ASP",
            "Web-CGI",
            "Web-Includes",
            "Web-Mgmt-Compat",
            "Web-Metabase",
            "Web-Lgcy-Scripting",
            "Web-WMI",
        })
        {
            StringAssert.Contains(script, $"'{feature}'");
        }

        StringAssert.Contains(script, "D:\\RevitServer\\2027");
        StringAssert.Contains(script, "S-1-5-20");
        StringAssert.Contains(script, "S-1-3-0");
        StringAssert.Contains(script, "Profile='Private'");
        StringAssert.Contains(script, "RemoteAddress='LocalSubnet'");
        StringAssert.Contains(script, "Port='80,808'");
        StringAssert.Contains(script, "RSN.ini");
        Assert.IsFalse(script.Contains("Start-Process", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("msiexec", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("ROLE_ACCELERATOR", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Plan_digest_comparison_is_bounded_and_exact()
    {
        var digest = new string('a', 64);
        Assert.IsTrue(WindowsRevitServerSetupOperator.FixedDigestEquals(digest, digest));
        Assert.IsFalse(WindowsRevitServerSetupOperator.FixedDigestEquals(digest, new string('b', 64)));
        Assert.IsFalse(WindowsRevitServerSetupOperator.FixedDigestEquals(digest, "short"));
    }

    [TestMethod]
    public void Health_passes_only_for_exact_Host_and_Admin_without_Accelerator()
    {
        var report = WindowsRevitServerHealthInspector.Evaluate(Healthy());

        Assert.AreEqual(RevitServerHealthStatus.Healthy, report.Status);
        Assert.AreEqual(9, report.Checks.Count);

        var wrongRoles = WindowsRevitServerHealthInspector.Evaluate(
            Healthy() with { RoleValue = "Host,Admin,Accelerator" });
        var accelerator = WindowsRevitServerHealthInspector.Evaluate(
            Healthy() with { AcceleratorPresent = true });

        Assert.AreEqual(RevitServerHealthStatus.Incomplete, wrongRoles.Status);
        Assert.AreEqual("roles_incorrect", wrongRoles.Checks.Single(check => check.Id == "roles").Code);
        Assert.AreEqual(RevitServerHealthStatus.Incomplete, accelerator.Status);
    }

    [TestMethod]
    public void Every_missing_or_ambiguous_health_observation_refuses_healthy()
    {
        var variants = new[]
        {
            Healthy() with { ProductCount = 0 },
            Healthy() with { ProductCount = 2 },
            Healthy() with { ProductVersion = "26.0.0.0" },
            Healthy() with { ProjectsPresent = false },
            Healthy() with { NetworkServiceAcl = false },
            Healthy() with { AppPoolIntegrated = false },
            Healthy() with { AdminRestApplication = false },
            Healthy() with { HostEndpointResponded = false },
            Healthy() with { RsnExact = false },
            Healthy() with { PrivateProfileOnly = false },
            Healthy() with { FirewallExact = false },
            Healthy() with { RepositoryShared = true },
            Healthy() with { FatalLogCount = 1 },
        };

        foreach (var variant in variants)
        {
            Assert.AreNotEqual(RevitServerHealthStatus.Healthy, WindowsRevitServerHealthInspector.Evaluate(variant).Status);
        }
    }

    [TestMethod]
    public void Health_script_inspects_all_Accelerator_scopes_and_expected_IIS_names()
    {
        var script = WindowsRevitServerHealthPowerShellSource.Script;
        StringAssert.Contains(script, "RSACCELERATOR2027', 'Machine'");
        StringAssert.Contains(script, "RSACCELERATOR2027', 'User'");
        StringAssert.Contains(script, "RSACCELERATOR2027', 'Process'");
        StringAssert.Contains(script, "RevitServerAppPool 2027 Release");
        StringAssert.Contains(script, "RevitServerAdminRESTService2027");
        StringAssert.Contains(script, "RevitServerAdmin2027");
        StringAssert.Contains(script, "RevitServer2027");
    }

    private static WindowsRevitServerHealthObservation Healthy() => new(
        1,
        "27.0.4.412",
        "Host,Admin",
        false,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        false,
        0);
}
