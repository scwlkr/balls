using System.Net;
using System.Reflection;
using Balls.Core;
using Balls.Host;
using Balls.Platform;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;
using Balls.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Balls.Daemon;

public static class DaemonHost
{
    public static async Task<DaemonInstance> StartAsync(
        DaemonOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var selection = HostPlatformSelector.SelectCurrent();
        if (selection is UnsupportedHostPlatform unsupported)
        {
            throw new PlatformNotSupportedException(unsupported.Message);
        }

        return await StartAsync(
            options,
            ((SupportedHostPlatform)selection).Platform,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<DaemonInstance> StartAsync(
        DaemonOptions options,
        HostPlatform host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(host);

        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);
        host.LocalControlServer.ValidateEndpoint(options.LocalControlEndpoint);
        if (string.IsNullOrWhiteSpace(options.NodeDisplayName))
        {
            throw new InputValidationException(
                "node_display_name_required",
                "Node display name is required.");
        }
        if (options.NodeDisplayName.Trim().Length > 100)
        {
            throw new InputValidationException(
                "node_display_name_too_long",
                "Node display name cannot exceed 100 characters.");
        }

        var securedDataDirectory = host.LocalState.Prepare(options.DataDirectory);
        var dataDirectoryLease = DataDirectoryLease.Acquire(securedDataDirectory);
        SqliteLocalStateStore? store = null;
        WebApplication? application = null;
        var endpointPrepared = false;

        try
        {
            store = await SqliteLocalStateStore
                .OpenAsync(securedDataDirectory, cancellationToken)
                .ConfigureAwait(false);
            var circleApplication = new CircleApplication(
                store,
                TimeProvider.System,
                options.NodeDisplayName);
            var browserAccess = new BrowserAccessBroker(
                TimeProvider.System,
                launchLifetime: TimeSpan.FromMinutes(1),
                sessionLifetime: TimeSpan.FromMinutes(30));
            var browserEndpoint = new BrowserEndpointState();
            await circleApplication.GetLocalNodeAsync(cancellationToken).ConfigureAwait(false);
            host.LocalState.Prepare(securedDataDirectory);
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
            BrowserAdapter.ConfigurePipeline(application, browserAccess, browserEndpoint);
            application.MapGet(
                ControlRoutes.Status,
                async (CancellationToken token) =>
                {
                    var node = await circleApplication.GetLocalNodeAsync(token).ConfigureAwait(false);
                    return new StatusResponse(
                        GetProductVersion(),
                        ControlProtocol.Version,
                        new NodeResponse(
                            node.Id.ToString(),
                            node.DisplayName,
                            node.CreatedAtUtc));
                })
                .Produces<StatusResponse>(StatusCodes.Status200OK);
            application.MapPost(
                    ControlRoutes.BrowserLaunch,
                    () =>
                    {
                        var launch = browserAccess.IssueLaunch(browserEndpoint.BaseUri);
                        return new LaunchBrowserResponse(
                            launch.Url.AbsoluteUri,
                            launch.ExpiresAtUtc);
                    })
                .Produces<LaunchBrowserResponse>(StatusCodes.Status200OK);
            application.MapPost(
                ControlRoutes.Circles,
                async (CreateCircleRequest request, CancellationToken token) =>
                {
                    if (!Guid.TryParse(request.RequestId, out var requestId))
                    {
                        return Results.BadRequest(
                            new ErrorResponse(
                                "invalid_request_id",
                                "Request ID must be a valid UUID."));
                    }

                    try
                    {
                        var circle = await circleApplication.CreateCircleAsync(
                            new CreateCircleCommand(
                                new CreationRequestId(requestId),
                                request.Name,
                                request.OwnerDisplayName),
                            token).ConfigureAwait(false);
                        var response = ToResponse(circle);
                        return Results.Created(ControlRoutes.Circle(response.Circle.Id), response);
                    }
                    catch (InputValidationException exception)
                    {
                        return Results.BadRequest(new ErrorResponse(exception.Code, exception.Message));
                    }
                    catch (LocalStateConflictException exception)
                    {
                        return Results.Conflict(new ErrorResponse(exception.Code, exception.Message));
                    }
                })
                .Produces<CircleDetailsResponse>(StatusCodes.Status201Created)
                .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
                .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
            application.MapGet(
                ControlRoutes.Circles,
                async (CancellationToken token) =>
                {
                    var circles = await circleApplication.ListCirclesAsync(token).ConfigureAwait(false);
                    return new CircleListResponse(circles.Select(ToSummary).ToArray());
                })
                .Produces<CircleListResponse>(StatusCodes.Status200OK);
            application.MapGet(
                ControlRoutes.Circles + "/{circleId}",
                async (string circleId, CancellationToken token) =>
                {
                    var lookup = await FindCircleAsync(circleApplication, circleId, token)
                        .ConfigureAwait(false);
                    return lookup.Error ?? Results.Ok(ToResponse(lookup.Circle!));
                })
                .Produces<CircleDetailsResponse>(StatusCodes.Status200OK)
                .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
                .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
            application.MapGet(
                ControlRoutes.Circles + "/{circleId}/members",
                async (string circleId, CancellationToken token) =>
                {
                    var lookup = await FindCircleAsync(circleApplication, circleId, token)
                        .ConfigureAwait(false);
                    return lookup.Error
                        ?? Results.Ok(
                            new MemberListResponse(
                                lookup.Circle!.Circle.Id.ToString(),
                                lookup.Circle.Members.Select(ToResponse).ToArray()));
                })
                .Produces<MemberListResponse>(StatusCodes.Status200OK)
                .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
                .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
            application.MapGet(
                ControlRoutes.Circles + "/{circleId}/nodes",
                async (string circleId, CancellationToken token) =>
                {
                    var lookup = await FindCircleAsync(circleApplication, circleId, token)
                        .ConfigureAwait(false);
                    return lookup.Error
                        ?? Results.Ok(
                            new NodeListResponse(
                                lookup.Circle!.Circle.Id.ToString(),
                                lookup.Circle.Nodes.Select(ToResponse).ToArray()));
                })
                .Produces<NodeListResponse>(StatusCodes.Status200OK)
                .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
                .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
            application.MapOpenApi(ControlRoutes.OpenApi);
            BrowserAdapter.MapRoutes(application, circleApplication, browserAccess);

            await application.StartAsync(cancellationToken).ConfigureAwait(false);
            browserEndpoint.Initialize(FindBrowserBaseUri(application));
            host.LocalControlServer.SecureEndpoint(options.LocalControlEndpoint);
            return new DaemonInstance(
                application,
                store,
                dataDirectoryLease,
                host.LocalControlServer,
                options.LocalControlEndpoint);
        }
        catch
        {
            try
            {
                if (application is not null)
                {
                    await application.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (store is not null)
                    {
                        await store.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (endpointPrepared)
                        {
                            host.LocalControlServer.CleanupEndpoint(options.LocalControlEndpoint);
                        }
                    }
                    finally
                    {
                        dataDirectoryLease.Dispose();
                    }
                }
            }

            throw;
        }
    }

    private static string GetProductVersion()
    {
        var informationalVersion = typeof(DaemonHost).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return informationalVersion?.Split('+', 2)[0]
            ?? typeof(DaemonHost).Assembly.GetName().Version?.ToString(3)
            ?? "unknown";
    }

    private static void ConfigureServer(
        KestrelServerOptions server,
        ILocalControlServerTransport transport,
        string endpoint)
    {
        server.Limits.MaxRequestBodySize = 32 * 1024;
        transport.ConfigureServer(server, endpoint);
        server.Listen(
            IPAddress.Loopback,
            0,
            listener => listener.Protocols = HttpProtocols.Http1);
    }

    private static Uri FindBrowserBaseUri(WebApplication application)
    {
        var addresses = application.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses
            ?? throw new InvalidOperationException("Kestrel did not report its bound addresses.");
        foreach (var address in addresses)
        {
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri)
                && IPAddress.TryParse(uri.Host, out var ipAddress)
                && IPAddress.IsLoopback(ipAddress))
            {
                return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
            }
        }

        throw new InvalidOperationException("ballsd did not bind a loopback browser listener.");
    }

    private static async Task<CircleLookup> FindCircleAsync(
        CircleApplication application,
        string value,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(value, out var circleId))
        {
            return new CircleLookup(
                null,
                Results.BadRequest(
                    new ErrorResponse(
                        "invalid_circle_id",
                        "Circle ID must be a valid UUID.")));
        }

        var circle = await application
            .GetCircleAsync(new CircleId(circleId), cancellationToken)
            .ConfigureAwait(false);
        return circle is null
            ? new CircleLookup(
                null,
                Results.NotFound(
                    new ErrorResponse(
                        "circle_not_found",
                        "The requested Circle is not known to this Node.")))
            : new CircleLookup(circle, null);
    }

    private static CircleDetailsResponse ToResponse(CircleDetails details)
    {
        return new CircleDetailsResponse(
            ToSummary(details),
            details.Members.Select(ToResponse).ToArray(),
            details.Nodes.Select(ToResponse).ToArray());
    }

    private static CircleResponse ToSummary(CircleDetails details)
    {
        return new CircleResponse(
            details.Circle.Id.ToString(),
            details.Circle.Name,
            details.Circle.CreatedAtUtc,
            details.Members.Count,
            details.Nodes.Count);
    }

    private static MemberResponse ToResponse(Member member)
    {
        return new MemberResponse(
            member.Id.ToString(),
            member.DisplayName,
            member.Role switch
            {
                MemberRole.Owner => "owner",
                _ => throw new InvalidOperationException($"Unknown Member role: {member.Role}."),
            },
            member.JoinedAtUtc);
    }

    private static CircleNodeResponse ToResponse(CircleNode node)
    {
        return new CircleNodeResponse(
            node.NodeId.ToString(),
            node.DisplayName,
            node.JoinedAtUtc);
    }

    private sealed record CircleLookup(CircleDetails? Circle, IResult? Error);
}

public sealed class DaemonInstance : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly SqliteLocalStateStore store;
    private readonly DataDirectoryLease dataDirectoryLease;
    private readonly ILocalControlServerTransport localControlServer;
    private readonly string localControlEndpoint;
    private int disposed;

    internal DaemonInstance(
        WebApplication application,
        SqliteLocalStateStore store,
        DataDirectoryLease dataDirectoryLease,
        ILocalControlServerTransport localControlServer,
        string localControlEndpoint)
    {
        this.application = application;
        this.store = store;
        this.dataDirectoryLease = dataDirectoryLease;
        this.localControlServer = localControlServer;
        this.localControlEndpoint = localControlEndpoint;
    }

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
                await store.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    localControlServer.CleanupEndpoint(localControlEndpoint);
                }
                finally
                {
                    dataDirectoryLease.Dispose();
                }
            }
        }
    }
}
