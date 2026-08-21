using System.Net;
using System.Reflection;
using Balls.Core;
using Balls.Host;
using Balls.Platform;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;
using Balls.Protocol.Remote.V1;
using Balls.Storage.Sqlite;
using Balls.Transport.Lan;
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

        var supported = (SupportedHostPlatform)selection;
        return await StartAsync(
            options,
            supported.Platform,
            supported.PrivateMaterialProtector,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<DaemonInstance> StartAsync(
        DaemonOptions options,
        HostPlatform host,
        IPrivateMaterialProtector privateMaterialProtector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(privateMaterialProtector);

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

        System.Net.IPEndPoint? admissionListenEndpoint = null;
        if (options.AdmissionListenEndpoint is not null)
        {
            try
            {
                admissionListenEndpoint = LanTcpEndpoint.Parse(
                    new RemoteTransportAddress(
                        LanTcpEndpoint.ProviderName,
                        options.AdmissionListenEndpoint));
            }
            catch (ArgumentException)
            {
                throw new InputValidationException(
                    "invalid_admission_listen_endpoint",
                    "Admission listening requires a numeric private or loopback IP address and non-zero port.");
            }
        }

        System.Net.IPEndPoint? messageListenEndpoint = null;
        if (options.MessageListenEndpoint is not null)
        {
            try
            {
                messageListenEndpoint = LanTcpEndpoint.Parse(
                    new RemoteTransportAddress(
                        LanTcpEndpoint.ProviderName,
                        options.MessageListenEndpoint));
            }
            catch (ArgumentException)
            {
                throw new InputValidationException(
                    "invalid_message_listen_endpoint",
                    "Message listening requires a numeric private or loopback IP address and non-zero port.");
            }
        }

        var securedDataDirectory = host.LocalState.Prepare(options.DataDirectory);
        var dataDirectoryLease = DataDirectoryLease.Acquire(securedDataDirectory);
        SqliteLocalStateStore? store = null;
        WebApplication? application = null;
        TcpLanTransportListener? admissionListener = null;
        CancellationTokenSource? admissionShutdown = null;
        Task? admissionTask = null;
        TcpLanTransportListener? messageListener = null;
        CancellationTokenSource? messageShutdown = null;
        Task? messageTask = null;
        var endpointPrepared = false;

        try
        {
            store = await SqliteLocalStateStore
                .OpenAsync(securedDataDirectory, privateMaterialProtector, cancellationToken)
                .ConfigureAwait(false);
            var circleApplication = new CircleApplication(
                store,
                TimeProvider.System,
                options.NodeDisplayName);
            var invitationApplication = new InvitationApplication(
                store,
                store,
                store,
                TimeProvider.System);
            var admissionApplication = new TrustedCircleAdmissionApplication(
                store,
                store,
                store,
                store,
                new TcpLanTransportConnector(),
                TimeProvider.System);
            var messageApplication = new TrustedCircleMessageApplication(
                store,
                store,
                store,
                store,
                new TcpLanTransportConnector(),
                TimeProvider.System);
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
            application.MapPost(
                ControlRoutes.CircleJoin,
                async (JoinCircleRequest request, CancellationToken token) =>
                {
                    try
                    {
                        var circle = await admissionApplication.JoinAsync(
                            request.Package,
                            new RemoteTransportAddress(
                                LanTcpEndpoint.ProviderName,
                                request.Endpoint),
                            request.MemberDisplayName,
                            token).ConfigureAwait(false);
                        return Results.Ok(ToResponse(circle));
                    }
                    catch (InputValidationException exception)
                    {
                        return Results.BadRequest(new ErrorResponse(exception.Code, exception.Message));
                    }
                    catch (ArgumentException)
                    {
                        return Results.BadRequest(
                            new ErrorResponse(
                                "invalid_admission_endpoint",
                                "Admission requires a numeric private or loopback IP address and port."));
                    }
                    catch (AdmissionRejectedException exception)
                    {
                        var error = new ErrorResponse(
                            exception.Code,
                            "The Circle admission was rejected.");
                        return exception.Code == "replayed"
                            ? Results.Conflict(error)
                            : Results.BadRequest(error);
                    }
                    catch (LocalStateConflictException exception)
                    {
                        return Results.Conflict(new ErrorResponse(exception.Code, exception.Message));
                    }
                    catch (RemoteChannelException exception)
                    {
                        return Results.Json(
                            new ErrorResponse(
                                exception.Code,
                                "The remote Anchor could not complete admission."),
                            statusCode: StatusCodes.Status502BadGateway);
                    }
                    catch (Exception exception) when (exception is
                        IOException or TimeoutException or System.Net.Sockets.SocketException)
                    {
                        return Results.Json(
                            new ErrorResponse(
                                "connection_failed",
                                "The remote Anchor could not be reached."),
                            statusCode: StatusCodes.Status502BadGateway);
                    }
                })
                .Produces<CircleDetailsResponse>(StatusCodes.Status200OK)
                .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
                .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
                .Produces<ErrorResponse>(StatusCodes.Status502BadGateway);
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
            application.MapGet(
                ControlRoutes.Circles + "/{circleId}/messages",
                async (string circleId, CancellationToken token) =>
                {
                    var lookup = await FindCircleAsync(circleApplication, circleId, token)
                        .ConfigureAwait(false);
                    if (lookup.Error is not null)
                    {
                        return lookup.Error;
                    }

                    var values = await store.ListCircleMessagesAsync(
                        lookup.Circle!.Circle.Id,
                        token).ConfigureAwait(false);
                    return Results.Ok(
                        new CircleMessageListResponse(
                            lookup.Circle.Circle.Id.ToString(),
                            values.Select(ToResponse).ToArray()));
                })
                .Produces<CircleMessageListResponse>(StatusCodes.Status200OK)
                .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
                .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
            application.MapPost(
                ControlRoutes.Circles + "/{circleId}/messages",
                async (string circleId, SendCircleMessageRequest request, CancellationToken token) =>
                {
                    if (!Guid.TryParseExact(circleId, "D", out var parsedCircleId)
                        || !Guid.TryParseExact(request.RequestId, "D", out var requestId))
                    {
                        return Results.BadRequest(
                            new ErrorResponse(
                                "invalid_request_id",
                                "Circle and message request IDs must be canonical UUIDs."));
                    }

                    try
                    {
                        var address = new RemoteTransportAddress(
                            LanTcpEndpoint.ProviderName,
                            request.Endpoint);
                        _ = LanTcpEndpoint.Parse(address);
                        var sent = await messageApplication.SendAsync(
                            new CircleMessageId(requestId),
                            new CircleId(parsedCircleId),
                            address,
                            request.Text,
                            token).ConfigureAwait(false);
                        return Results.Ok(ToResponse(sent));
                    }
                    catch (ArgumentException)
                    {
                        return Results.BadRequest(
                            new ErrorResponse(
                                "invalid_message_endpoint",
                                "Messaging requires a numeric private or loopback IP address and port."));
                    }
                    catch (CircleMessageRejectedException exception)
                    {
                        var error = new ErrorResponse(
                            exception.Code,
                            "The Circle message was rejected.");
                        return exception.Code is "conflict" or "replayed"
                            ? Results.Conflict(error)
                            : Results.BadRequest(error);
                    }
                    catch (LocalStateException exception)
                    {
                        return Results.BadRequest(new ErrorResponse(exception.Code, exception.Message));
                    }
                    catch (RemoteChannelException exception)
                    {
                        return Results.Json(
                            new ErrorResponse(
                                exception.Code,
                                "The remote Anchor could not accept the message."),
                            statusCode: StatusCodes.Status502BadGateway);
                    }
                    catch (Exception exception) when (exception is
                        IOException or TimeoutException or System.Net.Sockets.SocketException)
                    {
                        return Results.Json(
                            new ErrorResponse(
                                "connection_failed",
                                "The remote Anchor could not be reached."),
                            statusCode: StatusCodes.Status502BadGateway);
                    }
                })
                .Produces<CircleMessageResponse>(StatusCodes.Status200OK)
                .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
                .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
                .Produces<ErrorResponse>(StatusCodes.Status502BadGateway);
            application.MapPost(
                ControlRoutes.Circles + "/{circleId}/invitations",
                async (string circleId, CreateInvitationRequest request, CancellationToken token) =>
                {
                    if (!Guid.TryParseExact(circleId, "D", out var parsedCircleId))
                    {
                        return Results.BadRequest(
                            new ErrorResponse(
                                "invalid_circle_id",
                                "Circle ID must be a canonical UUID."));
                    }

                    try
                    {
                        var issued = await invitationApplication.CreateAsync(
                            new CircleId(parsedCircleId),
                            request.ValidForMinutes,
                            token).ConfigureAwait(false);
                        var response = new CreateInvitationResponse(
                            issued.CircleId.ToString(),
                            issued.InvitationId.ToString(),
                            issued.ExpiresAtUtc,
                            issued.Package);
                        return Results.Created(
                            ControlRoutes.Invitations + "/" + issued.InvitationId,
                            response);
                    }
                    catch (InputValidationException exception)
                    {
                        return Results.BadRequest(new ErrorResponse(exception.Code, exception.Message));
                    }
                    catch (LocalStateException exception) when (exception.Code == "circle_not_found")
                    {
                        return Results.NotFound(new ErrorResponse(exception.Code, exception.Message));
                    }
                })
                .Produces<CreateInvitationResponse>(StatusCodes.Status201Created)
                .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
                .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
            application.MapPost(
                ControlRoutes.Invitations + "/redeem",
                async (RedeemInvitationRequest request, CancellationToken token) =>
                {
                    try
                    {
                        var redeemed = await invitationApplication.RedeemAsync(request.Package, token)
                            .ConfigureAwait(false);
                        return Results.Ok(
                            new RedeemInvitationResponse(
                                redeemed.CircleId.ToString(),
                                redeemed.InvitationId.ToString(),
                                redeemed.RedemptionId.ToString(),
                                "accepted"));
                    }
                    catch (InvitationRejectedException exception)
                    {
                        var error = new ErrorResponse(
                            exception.Code,
                            "The Circle invitation was rejected.");
                        return exception.RejectionCode ==
                            Balls.Protocol.Remote.V1.InvitationRejectionCode.Replayed
                            ? Results.Conflict(error)
                            : Results.BadRequest(error);
                    }
                    catch (LocalStateException exception) when (exception.Code == "invitation_not_found")
                    {
                        return Results.NotFound(new ErrorResponse(exception.Code, exception.Message));
                    }
                })
                .Produces<RedeemInvitationResponse>(StatusCodes.Status200OK)
                .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
                .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
                .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
            application.MapOpenApi(ControlRoutes.OpenApi);
            BrowserAdapter.MapRoutes(application, circleApplication, store, browserAccess);

            await application.StartAsync(cancellationToken).ConfigureAwait(false);
            if (admissionListenEndpoint is not null)
            {
                admissionListener = new TcpLanTransportListener(admissionListenEndpoint);
                admissionShutdown = new CancellationTokenSource();
                admissionTask = RunAdmissionListenerAsync(
                    admissionListener,
                    admissionApplication,
                    admissionShutdown.Token);
            }
            if (messageListenEndpoint is not null)
            {
                messageListener = new TcpLanTransportListener(messageListenEndpoint);
                messageShutdown = new CancellationTokenSource();
                messageTask = RunMessageListenerAsync(
                    messageListener,
                    messageApplication,
                    messageShutdown.Token);
            }
            browserEndpoint.Initialize(FindBrowserBaseUri(application));
            host.LocalControlServer.SecureEndpoint(options.LocalControlEndpoint);
            return new DaemonInstance(
                application,
                store,
                dataDirectoryLease,
                host.LocalControlServer,
                options.LocalControlEndpoint,
                admissionListener,
                admissionShutdown,
                admissionTask,
                messageListener,
                messageShutdown,
                messageTask);
        }
        catch
        {
            try
            {
                admissionShutdown?.Cancel();
                messageShutdown?.Cancel();
                if (admissionListener is not null)
                {
                    await admissionListener.DisposeAsync().ConfigureAwait(false);
                }
                if (messageListener is not null)
                {
                    await messageListener.DisposeAsync().ConfigureAwait(false);
                }

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

    private static async Task RunAdmissionListenerAsync(
        TcpLanTransportListener listener,
        TrustedCircleAdmissionApplication application,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var connection in listener.AcceptAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                try
                {
                    await application.HandleAsync(connection, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (RemoteChannelException)
                {
                }
                finally
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task RunMessageListenerAsync(
        TcpLanTransportListener listener,
        TrustedCircleMessageApplication application,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var connection in listener.AcceptAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                try
                {
                    await application.HandleAsync(connection, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is
                    RemoteChannelException or LocalStateException)
                {
                }
                finally
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
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
                MemberRole.Member => "member",
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

    private static CircleMessageResponse ToResponse(PersistedCircleMessage message) =>
        new(
            message.Id.ToString(),
            message.CircleId.ToString(),
            message.AuthorMemberId.ToString(),
            message.AuthorNodeId.ToString(),
            message.Text,
            message.AuthoredAtUtc,
            message.Sequence,
            message.AcceptedAtUtc);

    private sealed record CircleLookup(CircleDetails? Circle, IResult? Error);
}

public sealed class DaemonInstance : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly SqliteLocalStateStore store;
    private readonly DataDirectoryLease dataDirectoryLease;
    private readonly ILocalControlServerTransport localControlServer;
    private readonly string localControlEndpoint;
    private readonly TcpLanTransportListener? admissionListener;
    private readonly CancellationTokenSource? admissionShutdown;
    private readonly Task? admissionTask;
    private readonly TcpLanTransportListener? messageListener;
    private readonly CancellationTokenSource? messageShutdown;
    private readonly Task? messageTask;
    private int disposed;

    internal DaemonInstance(
        WebApplication application,
        SqliteLocalStateStore store,
        DataDirectoryLease dataDirectoryLease,
        ILocalControlServerTransport localControlServer,
        string localControlEndpoint,
        TcpLanTransportListener? admissionListener,
        CancellationTokenSource? admissionShutdown,
        Task? admissionTask,
        TcpLanTransportListener? messageListener,
        CancellationTokenSource? messageShutdown,
        Task? messageTask)
    {
        this.application = application;
        this.store = store;
        this.dataDirectoryLease = dataDirectoryLease;
        this.localControlServer = localControlServer;
        this.localControlEndpoint = localControlEndpoint;
        this.admissionListener = admissionListener;
        this.admissionShutdown = admissionShutdown;
        this.admissionTask = admissionTask;
        this.messageListener = messageListener;
        this.messageShutdown = messageShutdown;
        this.messageTask = messageTask;
    }

    public RemoteTransportAddress? AdmissionAddress => admissionListener?.BoundAddress;

    public RemoteTransportAddress? MessageAddress => messageListener?.BoundAddress;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            admissionShutdown?.Cancel();
            messageShutdown?.Cancel();
            if (admissionListener is not null)
            {
                await admissionListener.DisposeAsync().ConfigureAwait(false);
            }
            if (messageListener is not null)
            {
                await messageListener.DisposeAsync().ConfigureAwait(false);
            }

            try
            {
                if (admissionTask is not null)
                {
                    await admissionTask.ConfigureAwait(false);
                }
                if (messageTask is not null)
                {
                    await messageTask.ConfigureAwait(false);
                }
            }
            finally
            {
                await application.StopAsync().ConfigureAwait(false);
                await application.DisposeAsync().ConfigureAwait(false);
            }
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
                    admissionShutdown?.Dispose();
                    messageShutdown?.Dispose();
                    dataDirectoryLease.Dispose();
                }
            }
        }
    }
}
