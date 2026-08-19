using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Balls.Platform.Linux;

[SupportedOSPlatform("linux")]
public sealed class LinuxUnixSocketControl :
    Balls.Platform.ILocalControlServerTransport,
    Balls.Platform.ILocalControlClientTransport
{
    private const int MaximumSocketPathBytes = 107;
    private const UnixFileMode PrivateSocketMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public void ValidateEndpoint(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (!Path.IsPathFullyQualified(endpoint)
            || !string.Equals(endpoint, Path.GetFullPath(endpoint), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Linux local-control socket must use a normalized absolute path.",
                nameof(endpoint));
        }

        if (Encoding.UTF8.GetByteCount(endpoint) > MaximumSocketPathBytes)
        {
            throw new ArgumentException(
                $"The Linux local-control socket path cannot exceed {MaximumSocketPathBytes} UTF-8 bytes.",
                nameof(endpoint));
        }
    }

    public void PrepareEndpoint(string endpoint)
    {
        ValidateEndpoint(endpoint);
        var parent = Path.GetDirectoryName(endpoint)
            ?? throw new ArgumentException("The socket path has no parent directory.", nameof(endpoint));
        LinuxDataDirectorySecurity.EnsurePrivateRuntimeDirectory(parent);

        var existing = LinuxNativeFileSystem.TryReadStatus(endpoint);
        if (existing is null)
        {
            return;
        }

        if (!existing.IsSocket || existing.UserId != LinuxNativeFileSystem.EffectiveUserId)
        {
            throw new UnauthorizedAccessException(
                "The local-control endpoint must be an owned Unix-domain socket.");
        }

        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(endpoint));
            throw new IOException("Another ballsd instance is already using the local-control socket.");
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionRefused)
        {
            File.Delete(endpoint);
        }
    }

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
    }

    public void ConfigureServer(KestrelServerOptions serverOptions, string endpoint)
    {
        ArgumentNullException.ThrowIfNull(serverOptions);
        ValidateEndpoint(endpoint);
        serverOptions.ListenUnixSocket(
            endpoint,
            listener => listener.Protocols = HttpProtocols.Http1);
    }

    public void SecureEndpoint(string endpoint)
    {
        var status = LinuxNativeFileSystem.ReadStatus(endpoint);
        if (!status.IsSocket || status.UserId != LinuxNativeFileSystem.EffectiveUserId)
        {
            throw new UnauthorizedAccessException(
                "The bound local-control socket is not owned by the current Linux user.");
        }

        File.SetUnixFileMode(endpoint, PrivateSocketMode);
    }

    public void CleanupEndpoint(string endpoint)
    {
        ValidateEndpoint(endpoint);
        var status = LinuxNativeFileSystem.TryReadStatus(endpoint);
        if (status is null)
        {
            return;
        }

        if (!status.IsSocket || status.UserId != LinuxNativeFileSystem.EffectiveUserId)
        {
            throw new UnauthorizedAccessException(
                "Refusing to remove a local-control endpoint that is not an owned socket.");
        }

        File.Delete(endpoint);
    }

    public HttpClient CreateClient(string endpoint, TimeSpan? timeout = null)
    {
        ValidateEndpoint(endpoint);
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(
                    AddressFamily.Unix,
                    SocketType.Stream,
                    ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(
                        new UnixDomainSocketEndPoint(endpoint),
                        cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("http://localhost", UriKind.Absolute),
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 256 * 1024,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
    }
}
