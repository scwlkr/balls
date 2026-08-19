using Balls.Cli;
using Balls.Core;
using Balls.Daemon;
using Balls.Platform.Windows;
using Balls.Protocol.Control.V1;
using Balls.Storage.Sqlite;

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
    public void Cli_depends_only_on_Protocol_and_the_local_transport_adapter()
    {
        AssertBallsReferences(
            typeof(CliApplication).Assembly,
            "Balls.Platform.Windows",
            "Balls.Protocol");
    }

    [TestMethod]
    public void Daemon_is_the_composition_root()
    {
        AssertBallsReferences(
            typeof(DaemonHost).Assembly,
            "Balls.Core",
            "Balls.Platform.Windows",
            "Balls.Protocol",
            "Balls.Storage.Sqlite");
    }

    [TestMethod]
    public void Windows_adapter_has_no_product_layer_dependency()
    {
        AssertBallsReferences(typeof(WindowsNamedPipeControl).Assembly);
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
