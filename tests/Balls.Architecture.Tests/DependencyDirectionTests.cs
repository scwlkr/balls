using Balls.Cli;
using Balls.Core;
using Balls.Daemon;
using Balls.Host;
using Balls.Platform;
using Balls.Platform.Linux;
using Balls.Platform.MacOS;
using Balls.Platform.Windows;
using Balls.Protocol.Control.V1;
using Balls.Security.Linux;
using Balls.Security.MacOS;
using Balls.Security.Windows;
using Balls.Storage.Sqlite;
using Balls.Transport.Lan;

namespace Balls.Architecture.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class DependencyDirectionTests
{
    [TestMethod]
    public void Core_has_no_outward_Balls_dependency()
    {
        AssertBallsReferences(typeof(CircleApplication).Assembly);
    }

    [TestMethod]
    public void Core_does_not_own_local_host_path_persistence()
    {
        var core = typeof(CircleApplication).Assembly;
        var storage = typeof(SqliteLocalStateStore).Assembly;

        Assert.IsNull(core.GetType("Balls.Core.CircleFilesHostedFolderBinding"));
        Assert.IsNull(core.GetType("Balls.Core.ICircleFilesHostedFolderStore"));
        Assert.IsNotNull(storage.GetType("Balls.Storage.Sqlite.CircleFilesHostedFolderBinding"));
        Assert.IsNotNull(storage.GetType("Balls.Storage.Sqlite.ICircleFilesHostedFolderStore"));
    }

    [TestMethod]
    public void Protocol_has_no_outward_Balls_dependency()
    {
        AssertBallsReferences(typeof(ControlProtocol).Assembly);
    }

    [TestMethod]
    public void Storage_depends_only_on_Core()
    {
        AssertBallsReferences(typeof(SqliteLocalStateStore).Assembly, "Balls.Core");
    }

    [TestMethod]
    public void Platform_contracts_have_no_product_layer_dependency()
    {
        AssertBallsReferences(typeof(HostPlatform).Assembly);
    }

    [TestMethod]
    public void Cli_depends_only_on_host_contracts_selection_and_Protocol()
    {
        AssertBallsReferences(
            typeof(CliApplication).Assembly,
            "Balls.Host",
            "Balls.Platform",
            "Balls.Protocol");
    }

    [TestMethod]
    public void Daemon_is_the_composition_root()
    {
        AssertBallsReferences(
            typeof(DaemonHost).Assembly,
            "Balls.Core",
            "Balls.Host",
            "Balls.Platform",
            "Balls.Protocol",
            "Balls.Storage.Sqlite",
            "Balls.Transport.Lan");
    }

    [TestMethod]
    public void Host_selection_depends_only_on_contracts_and_OS_adapters()
    {
        AssertBallsReferences(
            typeof(HostPlatformSelector).Assembly,
            "Balls.Core",
            "Balls.Platform",
            "Balls.Platform.Linux",
            "Balls.Platform.MacOS",
            "Balls.Platform.Windows",
            "Balls.Security.Linux",
            "Balls.Security.MacOS",
            "Balls.Security.Windows");
    }

    [TestMethod]
    public void Linux_adapter_depends_only_on_platform_contracts()
    {
        AssertBallsReferences(typeof(LinuxHostPlatform).Assembly, "Balls.Platform");
    }

    [TestMethod]
    public void Windows_adapter_depends_only_on_platform_contracts()
    {
        AssertBallsReferences(typeof(WindowsNamedPipeControl).Assembly, "Balls.Platform");
    }

    [TestMethod]
    public void MacOS_adapter_depends_only_on_platform_contracts()
    {
        AssertBallsReferences(typeof(MacOSHostPlatform).Assembly, "Balls.Platform");
    }

    [TestMethod]
    public void LAN_transport_depends_only_on_remote_protocol_contracts()
    {
        AssertBallsReferences(typeof(TcpLanTransportConnector).Assembly, "Balls.Protocol");
    }

    [TestMethod]
    public void Private_material_adapters_depend_only_on_Core_owned_contracts()
    {
        AssertBallsReferences(
            typeof(LinuxOwnedStatePrivateMaterialProtector).Assembly,
            "Balls.Core");
        AssertBallsReferences(
            typeof(MacOSOwnedStatePrivateMaterialProtector).Assembly,
            "Balls.Core");
        AssertBallsReferences(
            typeof(WindowsCurrentUserPrivateMaterialProtector).Assembly,
            "Balls.Core");
    }

    private static void AssertBallsReferences(
        System.Reflection.Assembly assembly,
        params string[] expected)
    {
        var actual = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name?.StartsWith("Balls.", StringComparison.Ordinal) == true)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var sortedExpected = expected.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        CollectionAssert.AreEqual(sortedExpected, actual);
    }
}
