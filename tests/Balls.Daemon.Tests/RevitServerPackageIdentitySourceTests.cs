using System.Text.Json;

namespace Balls.Daemon.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class RevitServerPackageIdentitySourceTests
{
    [TestMethod]
    public void Reads_only_exact_official_Development_Windows_installation_identity()
    {
        using var fixture = new InstallationFixture();
        fixture.Write(Document());

        var identity = new FileRevitServerPackageIdentitySource(fixture.Path).Load();

        Assert.AreEqual("development-20260827T120000Z-0123456789ab", identity.Tag);
        Assert.AreEqual("0123456789abcdef0123456789abcdef01234567", identity.Commit);
        Assert.AreEqual(new string('a', 64), identity.Sha256);
    }

    [TestMethod]
    public void Rejects_nonDevelopment_substituted_or_ambiguous_installation_identity()
    {
        foreach (var document in new[]
        {
            Document() with { Channel = "alpha" },
            Document() with { Release = new("development-20260827T120000Z-aaaaaaaaaaaa", "0123456789abcdef0123456789abcdef01234567") },
            Document() with { Package = Document().Package with { Sha256 = "short" } },
            Document() with { Package = Document().Package with { Platform = "linux" } },
            Document() with { ManifestUri = "https://example.invalid/development.json" },
            Document() with { Package = Document().Package with { Name = @"C:\temp\balls.zip" } },
        })
        {
            using var fixture = new InstallationFixture();
            fixture.Write(document);
            Assert.ThrowsExactly<InvalidDataException>(() =>
                new FileRevitServerPackageIdentitySource(fixture.Path).Load());
        }
    }

    [TestMethod]
    public void Rejects_unknown_fields_and_oversized_records()
    {
        using var fixture = new InstallationFixture();
        fixture.WriteRaw("{\"schemaVersion\":1,\"unexpected\":true}");
        Assert.ThrowsExactly<InvalidDataException>(() =>
            new FileRevitServerPackageIdentitySource(fixture.Path).Load());

        fixture.WriteRaw(new string('x', 33 * 1024));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            new FileRevitServerPackageIdentitySource(fixture.Path).Load());
    }

    private static InstallationDocument Document() => new(
        1,
        "Balls",
        "development",
        "https://balls.wlkrlabs.com/bootstrap/windows-x64.json",
        new DateTimeOffset(2026, 8, 27, 12, 1, 0, TimeSpan.Zero),
        new(
            "development-20260827T120000Z-0123456789ab",
            "0123456789abcdef0123456789abcdef01234567"),
        new(
            "balls-0.3.0-alpha.1-canary-windows-x64-0123456789ab.zip",
            new string('a', 64),
            "0.3.0-alpha.1",
            "windows",
            "x64"));

    private sealed class InstallationFixture : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"balls-installation-{Guid.NewGuid():N}");

        public InstallationFixture()
        {
            Directory.CreateDirectory(directory);
            Path = System.IO.Path.Combine(directory, "installation.json");
        }

        public string Path { get; }

        public void Write(InstallationDocument document) =>
            WriteRaw(JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        public void WriteRaw(string value) => File.WriteAllText(Path, value);

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }

    private sealed record InstallationDocument(
        int SchemaVersion,
        string Product,
        string Channel,
        string ManifestUri,
        DateTimeOffset InstalledAt,
        ReleaseIdentity Release,
        PackageIdentity Package);
    private sealed record ReleaseIdentity(string Tag, string Commit);
    private sealed record PackageIdentity(
        string Name,
        string Sha256,
        string Version,
        string Platform,
        string Architecture);
}
