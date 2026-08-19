using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;
using Microsoft.Extensions.DependencyInjection;

namespace Balls.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class WindowsNamedPipeDefaults
{
    public static string GetCurrentUserPipeName()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current Windows account has no security identifier.");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sid));
        return $"balls-control-{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}";
    }
}

[SupportedOSPlatform("windows")]
public static class WindowsNamedPipeControl
{
    public static void ConfigureServer(KestrelServerOptions serverOptions, string pipeName)
    {
        ArgumentNullException.ThrowIfNull(serverOptions);
        ValidatePipeName(pipeName);

        serverOptions.ListenNamedPipe(
            pipeName,
            endpoint => endpoint.Protocols = HttpProtocols.Http1);
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure<NamedPipeTransportOptions>(
            options => options.CurrentUserOnly = true);
    }

    public static void ValidatePipeName(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (pipeName.IndexOfAny(['\\', '/']) >= 0)
        {
            throw new ArgumentException("The pipe name cannot contain path separators.", nameof(pipeName));
        }
    }
}

[SupportedOSPlatform("windows")]
public static class WindowsNamedPipeHttpClient
{
    public static HttpClient Create(string pipeName, TimeSpan? timeout = null)
    {
        WindowsNamedPipeControl.ValidatePipeName(pipeName);

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var pipe = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous
                        | PipeOptions.WriteThrough
                        | PipeOptions.CurrentUserOnly,
                    TokenImpersonationLevel.Anonymous);
                try
                {
                    await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    return pipe;
                }
                catch
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
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
