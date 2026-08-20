using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Balls.Protocol.Remote.V1;
using Balls.Transport.Lan;

namespace Balls.RemoteHarness;

internal static class RemoteHarnessCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        MaxDepth = 16,
    };

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return args switch
            {
                ["prepare", var directory] => await PrepareAsync(directory).ConfigureAwait(false),
                ["server", var configuration, var endpoint, var readyFile] =>
                    await ServeAsync(configuration, endpoint, readyFile).ConfigureAwait(false),
                ["client", var configuration, var endpoint] =>
                    await ConnectAsync(configuration, endpoint).ConfigureAwait(false),
                _ => Usage(),
            };
        }
        catch (RemoteChannelException exception)
        {
            Console.Error.WriteLine(exception.Code);
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("timeout");
            return 1;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            CryptographicException or
            IOException or
            JsonException or
            SocketException or
            TimeoutException)
        {
            Console.Error.WriteLine(exception.GetType().Name);
            return 1;
        }
    }

    private static async Task<int> PrepareAsync(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(fullDirectory);
        var serverPath = Path.Combine(fullDirectory, "server.json");
        var clientPath = Path.Combine(fullDirectory, "client.json");
        if (File.Exists(serverPath) || File.Exists(clientPath))
        {
            throw new IOException("Harness configuration already exists.");
        }

        var pair = HarnessIdentity.CreatePair();
        await WriteNewAsync(serverPath, pair.Server).ConfigureAwait(false);
        await WriteNewAsync(clientPath, pair.Client).ConfigureAwait(false);
        Console.WriteLine("prepared");
        return 0;
    }

    private static async Task<int> ServeAsync(
        string configurationPath,
        string endpointValue,
        string readyFile)
    {
        var configuration = await ReadConfigurationAsync(configurationPath).ConfigureAwait(false);
        if (!IPEndPoint.TryParse(endpointValue, out var endpoint))
        {
            throw new ArgumentException("A numeric server endpoint is required.");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var certificate = HarnessIdentity.LoadCertificate(configuration);
        var localIdentity = HarnessIdentity.CreateLocalIdentity(configuration, certificate);
        var expectedPeer = HarnessIdentity.CreatePeerExpectation(configuration);
        await using var listener = new TcpLanTransportListener(endpoint);
        await WriteNewTextAsync(
            Path.GetFullPath(readyFile),
            listener.BoundAddress.Value).ConfigureAwait(false);

        await foreach (var connection in listener.AcceptAsync(timeout.Token))
        {
            await using var ownedConnection = connection;
            await using var channel = await RemoteAuthenticatedChannel.AcceptAsync(
                connection,
                localIdentity,
                [expectedPeer],
                cancellationToken: timeout.Token).ConfigureAwait(false);
            var request = await channel.ReadAsync(timeout.Token).ConfigureAwait(false);
            if (!request.Payload.AsSpan().SequenceEqual("ping"u8))
            {
                throw new InvalidDataException("The harness request payload is invalid.");
            }

            await channel.WriteAsync(
                new RemoteFrame(Guid.CreateVersion7(), "pong"u8.ToArray()),
                timeout.Token).ConfigureAwait(false);
            WriteResult("received", channel);
            return 0;
        }

        throw new InvalidOperationException("The LAN listener ended before accepting a peer.");
    }

    private static async Task<int> ConnectAsync(string configurationPath, string endpointValue)
    {
        var configuration = await ReadConfigurationAsync(configurationPath).ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var certificate = HarnessIdentity.LoadCertificate(configuration);
        var connector = new TcpLanTransportConnector(TimeSpan.FromSeconds(10));
        await using var connection = await connector.ConnectAsync(
            new RemoteTransportAddress(LanTcpEndpoint.ProviderName, endpointValue),
            timeout.Token).ConfigureAwait(false);
        await using var channel = await RemoteAuthenticatedChannel.ConnectAsync(
            connection,
            configuration.PeerDnsName,
            HarnessIdentity.CreateLocalIdentity(configuration, certificate),
            HarnessIdentity.CreatePeerExpectation(configuration),
            cancellationToken: timeout.Token).ConfigureAwait(false);
        await channel.WriteAsync(
            new RemoteFrame(Guid.CreateVersion7(), "ping"u8.ToArray()),
            timeout.Token).ConfigureAwait(false);
        var response = await channel.ReadAsync(timeout.Token).ConfigureAwait(false);
        if (!response.Payload.AsSpan().SequenceEqual("pong"u8))
        {
            throw new InvalidDataException("The harness response payload is invalid.");
        }

        WriteResult("acknowledged", channel);
        return 0;
    }

    private static async Task<HarnessConfiguration> ReadConfigurationAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length is 0 or > 512 * 1024)
        {
            throw new InvalidDataException("The harness configuration is outside its bounds.");
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<HarnessConfiguration>(
            stream,
            JsonOptions).ConfigureAwait(false)
            ?? throw new InvalidDataException("The harness configuration is invalid.");
    }

    private static async Task WriteNewAsync(string path, HarnessConfiguration configuration)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions)
            .ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
        RestrictUnixPermissions(path);
    }

    private static async Task WriteNewTextAsync(string path, string value)
    {
        var temporaryPath = $"{path}.{Guid.CreateVersion7():N}.tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         FileOptions.Asynchronous))
        {
            var encoded = Encoding.UTF8.GetBytes(value);
            await stream.WriteAsync(encoded).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        File.Move(temporaryPath, path);
    }

    private static void RestrictUnixPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void WriteResult(string status, RemoteAuthenticatedChannel channel)
    {
        Console.WriteLine(
            JsonSerializer.Serialize(
                new HarnessResult(
                    status,
                    channel.Provider,
                    channel.CircleId,
                    channel.PeerNodeId,
                    channel.NegotiatedProtocolVersion,
                    channel.IsEncrypted),
                JsonOptions));
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            "Usage: Balls.RemoteHarness prepare <directory> | server <config> <ip:port> <ready-file> | client <config> <ip:port>");
        return 2;
    }
}
