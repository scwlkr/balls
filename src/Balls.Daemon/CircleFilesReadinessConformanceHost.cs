using System.Net;
using Balls.Platform;
using Balls.Protocol.Control.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;

namespace Balls.Daemon;

internal static class CircleFilesReadinessConformanceHost
{
    public static async Task<CircleFilesReadinessConformanceInstance> StartAsync(
        DaemonOptions options,
        HostPlatform host,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);
        host.LocalControlServer.ValidateEndpoint(options.LocalControlEndpoint);

        var securedDataDirectory = host.LocalState.Prepare(options.DataDirectory);
        var lease = DataDirectoryLease.Acquire(securedDataDirectory);
        WebApplication? application = null;
        var endpointPrepared = false;
        try
        {
            host.LocalControlServer.PrepareEndpoint(options.LocalControlEndpoint);
            endpointPrepared = true;
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(DaemonHost).Assembly.GetName().Name,
                EnvironmentName = Environments.Production,
            });
            builder.Configuration.Sources.Clear();
            host.LocalControlServer.ConfigureServices(builder.Services);
            builder.WebHost.ConfigureKestrel(
                server => ConfigureServer(
                    server,
                    host.LocalControlServer,
                    options.LocalControlEndpoint));
            builder.Services.AddOpenApi();
            builder.Services.ConfigureHttpJsonOptions(
                json => ControlJson.Configure(json.SerializerOptions));

            application = builder.Build();
            application.MapGet(
                    ControlRoutes.CircleFilesReadiness,
                    async (CancellationToken token) =>
                        CircleFilesResponseMapper.ToResponse(
                            await host.CircleFilesReadiness
                                .InspectAsync(token)
                                .ConfigureAwait(false)))
                .Produces<CircleFilesReadinessResponse>(StatusCodes.Status200OK);
            await application.StartAsync(cancellationToken).ConfigureAwait(false);
            host.LocalControlServer.SecureEndpoint(options.LocalControlEndpoint);
            return new CircleFilesReadinessConformanceInstance(
                application,
                lease,
                host.LocalControlServer,
                options.LocalControlEndpoint);
        }
        catch
        {
            if (application is not null)
            {
                await application.DisposeAsync().ConfigureAwait(false);
            }

            if (endpointPrepared)
            {
                host.LocalControlServer.CleanupEndpoint(options.LocalControlEndpoint);
            }

            lease.Dispose();
            throw;
        }
    }

    private static void ConfigureServer(
        KestrelServerOptions server,
        ILocalControlServerTransport transport,
        string endpoint)
    {
        server.Limits.MaxRequestBodySize = 8 * 1024;
        transport.ConfigureServer(server, endpoint);
    }
}

internal sealed class CircleFilesReadinessConformanceInstance(
    WebApplication application,
    DataDirectoryLease lease,
    ILocalControlServerTransport localControlServer,
    string localControlEndpoint) : IAsyncDisposable
{
    private int disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await application.StopAsync().ConfigureAwait(false);
            await application.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                localControlServer.CleanupEndpoint(localControlEndpoint);
            }
            finally
            {
                lease.Dispose();
            }
        }
    }
}
