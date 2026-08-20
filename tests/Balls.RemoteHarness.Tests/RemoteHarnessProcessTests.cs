using System.Diagnostics;
using System.Text.Json;
using Balls.RemoteHarness;

namespace Balls.RemoteHarness.Tests;

[TestClass]
[TestCategory("ProcessIntegration")]
public sealed class RemoteHarnessProcessTests
{
    [TestMethod]
    public async Task Separate_processes_establish_an_authenticated_encrypted_LAN_channel()
    {
        using var directory = new TemporaryDirectory();
        var prepared = await RunAsync("prepare", directory.Path);
        Assert.AreEqual(0, prepared.ExitCode, prepared.StandardError);
        Assert.AreEqual("prepared", prepared.StandardOutput);
        var serverConfig = Path.Combine(directory.Path, "server.json");
        var clientConfig = Path.Combine(directory.Path, "client.json");
        var readyFile = Path.Combine(directory.Path, "ready.txt");
        using var server = Start("server", serverConfig, "127.0.0.1:0", readyFile);
        try
        {
            var endpoint = await WaitForReadyAsync(server, readyFile);
            var client = await RunAsync("client", clientConfig, endpoint);
            var served = await CompleteAsync(server);

            Assert.AreEqual(0, client.ExitCode, client.StandardError);
            Assert.AreEqual(0, served.ExitCode, served.StandardError);
            var clientResult = Deserialize(client.StandardOutput);
            var serverResult = Deserialize(served.StandardOutput);
            Assert.AreEqual("acknowledged", clientResult.Status);
            Assert.AreEqual("received", serverResult.Status);
            Assert.AreEqual(clientResult.CircleId, serverResult.CircleId);
            Assert.AreNotEqual(clientResult.PeerNodeId, serverResult.PeerNodeId);
            Assert.AreEqual("lan-tcp-v1", clientResult.Provider);
            Assert.AreEqual(1, clientResult.ProtocolVersion);
            Assert.IsTrue(clientResult.Encrypted);
            Assert.IsTrue(serverResult.Encrypted);
        }
        finally
        {
            if (!server.HasExited)
            {
                server.Kill(entireProcessTree: true);
                await server.WaitForExitAsync();
            }
        }
    }

    private static Process Start(params string[] arguments)
    {
        var executable = Path.Combine(
            Path.GetDirectoryName(typeof(HarnessMarker).Assembly.Location)!,
            OperatingSystem.IsWindows() ? "Balls.RemoteHarness.exe" : "Balls.RemoteHarness");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the remote harness.");
    }

    private static async Task<ProcessResult> RunAsync(params string[] arguments)
    {
        using var process = Start(arguments);
        return await CompleteAsync(process);
    }

    private static async Task<ProcessResult> CompleteAsync(Process process)
    {
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        return new ProcessResult(
            process.ExitCode,
            (await output).Trim(),
            (await error).Trim());
    }

    private static async Task<string> WaitForReadyAsync(Process server, string readyFile)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            if (server.HasExited)
            {
                var error = await server.StandardError.ReadToEndAsync();
                Assert.Fail($"Server exited before readiness: {error}");
            }

            if (File.Exists(readyFile))
            {
                var endpoint = (await File.ReadAllTextAsync(readyFile, timeout.Token)).Trim();
                if (endpoint.Length > 0)
                {
                    return endpoint;
                }
            }

            await Task.Delay(25, timeout.Token);
        }

        throw new TimeoutException("The remote harness did not become ready.");
    }

    private static HarnessOutput Deserialize(string json) =>
        JsonSerializer.Deserialize<HarnessOutput>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new AssertFailedException("The harness output was empty.");

    private sealed record HarnessOutput(
        string Status,
        string Provider,
        string CircleId,
        string PeerNodeId,
        int ProtocolVersion,
        bool Encrypted);

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"balls-remote-{Guid.CreateVersion7():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
