using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Balls.Platform;

public sealed record HostDefaults(
    string DataDirectory,
    string LocalControlEndpoint,
    string NodeDisplayName,
    string LocalControlListenerDescription,
    string LocalControlEndpointDescription);

public interface ILocalStatePreparer
{
    string Prepare(string dataDirectory);
}

public interface ILocalControlServerTransport
{
    void ValidateEndpoint(string endpoint);

    void ConfigureServices(IServiceCollection services);

    void ConfigureServer(KestrelServerOptions serverOptions, string endpoint);
}

public interface ILocalControlClientTransport
{
    HttpClient CreateClient(string endpoint, TimeSpan? timeout = null);
}

public sealed record HostPlatform(
    HostDefaults Defaults,
    ILocalStatePreparer LocalState,
    ILocalControlServerTransport LocalControlServer,
    ILocalControlClientTransport LocalControlClient);
