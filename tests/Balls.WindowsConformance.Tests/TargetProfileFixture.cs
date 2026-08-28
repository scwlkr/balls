using System.Text.Json;

namespace Balls.WindowsConformance.Tests;

internal sealed class TargetProfileFixture : IDisposable
{
    private TargetProfileFixture(string directory, string path)
    {
        Directory = directory;
        Path = path;
    }

    public string Directory { get; }

    public string Path { get; }

    public static TargetProfileFixture Create(
        bool authorized = true,
        string host = "127.0.0.1",
        string? productHost = null,
        int productPort = 22264,
        string operation = "windows-smb-readiness-v1",
        string? disposablePath = null,
        string? expectedVolumeIdentitySha256 = null,
        string? expectedDiskIdentitySha256 = null)
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"balls-conformance-target-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(directory);
        var knownHosts = System.IO.Path.Combine(directory, "known_hosts");
        var publicKey = System.IO.Path.Combine(directory, "balls.pub");
        File.WriteAllText(knownHosts, "bounded test host key");
        File.WriteAllText(publicKey, "bounded test public key");
        var path = System.IO.Path.Combine(directory, "target.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(new
            {
                schema = "balls-windows-conformance-target-v1",
                operation,
                authorized,
                targetId = "disposable-windows-lab",
                expectedComputerName = "BALLS-LAB",
                expectedAccountKind = "administrator",
                expectedProductAccountSidSha256 = new string('a', 64),
                connectivityPath = "loopback-only OpenSSH forward to private disposable guest",
                disposablePath,
                expectedVolumeIdentitySha256 = operation == "windows-circle-files-host-v1"
                    ? expectedVolumeIdentitySha256 ?? new string('b', 64)
                    : expectedVolumeIdentitySha256,
                expectedDiskIdentitySha256 = operation == "windows-circle-files-host-v1"
                    ? expectedDiskIdentitySha256 ?? new string('c', 64)
                    : expectedDiskIdentitySha256,
                transport = new
                {
                    host,
                    port = 22264,
                    user = "ballsverify",
                    knownHostsFile = knownHosts,
                    publicKeyFile = publicKey,
                },
                productTransport = new
                {
                    host = productHost ?? host,
                    port = productPort,
                    user = "ballsproduct",
                    knownHostsFile = knownHosts,
                    publicKeyFile = publicKey,
                },
            }));
        return new TargetProfileFixture(directory, path);
    }

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
}
