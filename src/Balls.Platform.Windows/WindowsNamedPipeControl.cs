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
                    PipeAccessRights.ReadWrite | PipeAccessRights.ReadPermissions,
                    PipeOptions.Asynchronous
                        | PipeOptions.WriteThrough,
                    TokenImpersonationLevel.Anonymous,
                    HandleInheritability.None);
                try
                {
                    await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    ValidateServerIdentity(pipe);
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

    private static void ValidateServerIdentity(NamedPipeClientStream pipe)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        var serverOwner = pipe
            .GetAccessControl()
            .GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        WindowsLocalControlIdentity.ValidateServerIdentity(
            identity.User?.Value,
            identity.Owner?.Value,
            serverOwner?.Value,
            principal.IsInRole(WindowsBuiltInRole.Administrator));
    }
}

internal static class WindowsLocalControlIdentity
{
    internal static void ValidateServerIdentity(
        string? currentUserSid,
        string? currentOwnerSid,
        string? serverOwnerSid,
        bool currentProcessElevated)
    {
        var expectedOwnerSid = currentProcessElevated ? currentOwnerSid : currentUserSid;
        if (string.IsNullOrWhiteSpace(expectedOwnerSid)
            || !string.Equals(expectedOwnerSid, serverOwnerSid, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The selected local control is not owned by this user at the current elevation.");
        }
    }
}
