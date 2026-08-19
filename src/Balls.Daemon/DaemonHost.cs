using System.Reflection;
using Balls.Core;
using Balls.Platform.Windows;
using Balls.Protocol.Control.V1;
using Balls.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
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
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Phase 1 currently provides a Windows named-pipe local control transport.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);
        WindowsNamedPipeControl.ValidatePipeName(options.PipeName);
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

        var securedDataDirectory = WindowsDataDirectorySecurity.Prepare(options.DataDirectory);
        var dataDirectoryLease = DataDirectoryLease.Acquire(securedDataDirectory);
        SqliteLocalStateStore? store = null;
        WebApplication? application = null;

        try
        {
            store = await SqliteLocalStateStore
                .OpenAsync(securedDataDirectory, cancellationToken)
                .ConfigureAwait(false);
            var circleApplication = new CircleApplication(
                store,
                TimeProvider.System,
                options.NodeDisplayName);
            await circleApplication.GetLocalNodeAsync(cancellationToken).ConfigureAwait(false);
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(DaemonHost).Assembly.GetName().Name,
                EnvironmentName = Environments.Production,
            });
            builder.Configuration.Sources.Clear();
            WindowsNamedPipeControl.ConfigureServices(builder.Services);
            builder.WebHost.ConfigureKestrel(
                server => ConfigureWindowsServer(server, options.PipeName));
            builder.Services.AddOpenApi();
            builder.Services.ConfigureHttpJsonOptions(json =>
            {
                json.SerializerOptions.PropertyNamingPolicy = ControlJson.Options.PropertyNamingPolicy;
                json.SerializerOptions.PropertyNameCaseInsensitive =
                    ControlJson.Options.PropertyNameCaseInsensitive;
                json.SerializerOptions.UnmappedMemberHandling =
                    ControlJson.Options.UnmappedMemberHandling;
            });

            application = builder.Build();
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

            await application.StartAsync(cancellationToken).ConfigureAwait(false);
            return new DaemonInstance(application, store, dataDirectoryLease);
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
                    dataDirectoryLease.Dispose();
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

    private static void ConfigureWindowsServer(
        Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions server,
        string pipeName)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        server.Limits.MaxRequestBodySize = 32 * 1024;
        WindowsNamedPipeControl.ConfigureServer(server, pipeName);
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
    private int disposed;

    internal DaemonInstance(
        WebApplication application,
        SqliteLocalStateStore store,
        DataDirectoryLease dataDirectoryLease)
    {
        this.application = application;
        this.store = store;
        this.dataDirectoryLease = dataDirectoryLease;
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
                dataDirectoryLease.Dispose();
            }
        }
    }
}
