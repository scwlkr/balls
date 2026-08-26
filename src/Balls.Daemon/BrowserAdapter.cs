using System.Net;
using System.Reflection;
using Balls.Core;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;
using Microsoft.Extensions.FileProviders;

namespace Balls.Daemon;

internal static class BrowserAdapter
{
    private const string SessionCookieName = "__Host-balls-session";
    private const string AntiforgeryHeaderName = "X-Balls-Antiforgery";
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; font-src 'self'; "
        + "connect-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; "
        + "frame-ancestors 'none'; form-action 'self'";

    public static void ConfigurePipeline(
        WebApplication application,
        BrowserAccessBroker access,
        BrowserEndpointState endpoint)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(endpoint);

        application.Use(async (context, next) =>
        {
            var isTcp = context.Connection.LocalIpAddress is not null
                || context.Connection.RemoteIpAddress is not null;
            if (!isTcp)
            {
                if (!context.Request.Path.StartsWithSegments(ControlRoutes.BasePath))
                {
                    await WriteErrorAsync(
                        context,
                        StatusCodes.Status404NotFound,
                        "not_found",
                        "The requested local-control resource does not exist.");
                    return;
                }

                await next(context);
                return;
            }

            if (!IsLoopback(context.Connection.LocalIpAddress)
                || !IsLoopback(context.Connection.RemoteIpAddress))
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "browser_forbidden",
                    "The browser interface accepts loopback connections only.");
                return;
            }
            if (context.Request.Path.StartsWithSegments(ControlRoutes.BasePath))
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status404NotFound,
                    "not_found",
                    "The requested browser resource does not exist.");
                return;
            }
            if (!string.Equals(
                    context.Request.Host.Value,
                    endpoint.Authority,
                    StringComparison.OrdinalIgnoreCase))
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "invalid_host",
                    "The browser request Host is invalid.");
                return;
            }

            AddSecurityHeaders(context.Response);
            var origins = context.Request.Headers.Origin;
            var originRequired = !HttpMethods.IsGet(context.Request.Method)
                && !HttpMethods.IsHead(context.Request.Method)
                && !HttpMethods.IsOptions(context.Request.Method);
            if (origins.Count > 1
                || (originRequired && origins.Count != 1)
                || (origins.Count == 1
                    && !string.Equals(origins[0], endpoint.Origin, StringComparison.Ordinal)))
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "invalid_origin",
                    "The browser request Origin is invalid.");
                return;
            }

            var isSessionBootstrap = context.GetEndpoint()?
                .Metadata
                .GetMetadata<BrowserSessionBootstrapMetadata>() is not null;
            if (context.Request.Path.StartsWithSegments(BrowserRoutes.BasePath)
                && !isSessionBootstrap)
            {
                var sessionToken = context.Request.Cookies[SessionCookieName];
                if (!access.IsSessionAuthorized(sessionToken))
                {
                    await WriteErrorAsync(
                        context,
                        StatusCodes.Status401Unauthorized,
                        "browser_session_required",
                        "A valid browser session is required.");
                    return;
                }
                if (originRequired
                    && !access.IsStateChangeAuthorized(
                        sessionToken,
                        context.Request.Headers[AntiforgeryHeaderName].SingleOrDefault()))
                {
                    await WriteErrorAsync(
                        context,
                        StatusCodes.Status403Forbidden,
                        "antiforgery_required",
                        "A valid antiforgery token is required.");
                    return;
                }
            }

            await next(context);
        });

        var webRoot = FindWebRoot();
        var indexPath = Path.Combine(webRoot, "index.html");
        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException("The Balls browser bundle is missing.", indexPath);
        }

        var fileProvider = new PhysicalFileProvider(webRoot);
        application.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        application.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
    }

    public static void MapRoutes(
        WebApplication application,
        CircleApplication circleApplication,
        CircleMessageQueryApplication messageQueries,
        CircleFilesApplication filesApplication,
        BrowserCircleFilesContributionApplication filesContributionApplication,
        CircleFilesMemberMappingApplication filesMemberMappingApplication,
        TrustedCircleFilesSyncApplication filesSyncApplication,
        IAdmissionStateStore circleConnections,
        InvitationApplication invitationApplication,
        TrustedCircleAdmissionApplication admissionApplication,
        BrowserInvitationListenerState invitationListeners,
        BrowserAccessBroker access)
    {
        application.MapPost(
                BrowserRoutes.Session,
                (ExchangeBrowserSessionRequest request, HttpContext context) =>
                {
                    var session = access.ExchangeLaunchCapability(request.Capability);
                    if (session is null)
                    {
                        return Results.Json(
                            new ErrorResponse(
                                "invalid_launch_capability",
                                "The browser launch capability is invalid, expired, or already used."),
                            ControlJson.Options,
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    context.Response.Cookies.Append(
                        SessionCookieName,
                        session.SessionToken,
                        new CookieOptions
                        {
                            HttpOnly = true,
                            Secure = true,
                            SameSite = SameSiteMode.Strict,
                            Path = "/",
                            MaxAge = session.ExpiresAtUtc - DateTimeOffset.UtcNow,
                            IsEssential = true,
                        });
                    return Results.Ok(
                        new BrowserSessionResponse(
                            session.AntiforgeryToken,
                            session.ExpiresAtUtc));
                })
            .WithMetadata(BrowserSessionBootstrapMetadata.Instance)
            .Produces<BrowserSessionResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);
        application.MapGet(
                BrowserRoutes.Status,
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
        application.MapGet(
                BrowserRoutes.Circles,
                async (CancellationToken token) =>
                {
                    var circles = await circleApplication.ListCirclesAsync(token).ConfigureAwait(false);
                    return new CircleListResponse(circles.Select(ToSummary).ToArray());
                })
            .Produces<CircleListResponse>(StatusCodes.Status200OK);
        application.MapPost(
                BrowserRoutes.CircleJoin,
                (JoinBrowserCircleRequest request, CancellationToken token) =>
                    BrowserInvitationEndpoints.JoinAsync(admissionApplication, request, token))
            .Produces<CircleDetailsResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status502BadGateway);
        application.MapPost(
                BrowserRoutes.Circles + "/{circleId}/invitations",
                (string circleId, CreateBrowserCircleInvitationRequest request, CancellationToken token) =>
                    BrowserInvitationEndpoints.CreateAsync(
                        invitationApplication,
                        invitationListeners,
                        circleId,
                        request,
                        token))
            .Produces<BrowserCircleInvitationResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
        application.MapPost(
                BrowserRoutes.Circles,
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
                        return Results.Created(BrowserRoutes.Circle(response.Circle.Id), response);
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
                BrowserRoutes.Circles + "/{circleId}",
                async (string circleId, CancellationToken token) =>
                {
                    if (!Guid.TryParse(circleId, out var parsedCircleId))
                    {
                        return Results.BadRequest(
                            new ErrorResponse(
                                "invalid_circle_id",
                                "Circle ID must be a valid UUID."));
                    }

                    var circle = await circleApplication
                        .GetCircleAsync(new CircleId(parsedCircleId), token)
                        .ConfigureAwait(false);
                    return circle is null
                        ? Results.NotFound(
                            new ErrorResponse(
                                "circle_not_found",
                                "The requested Circle is not known to this Node."))
                        : Results.Ok(ToResponse(circle));
                })
            .Produces<CircleDetailsResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
        application.MapPost(
                BrowserRoutes.Circles + "/{circleId}/files/sync",
                (string circleId, CancellationToken token) =>
                    BrowserCircleFilesSyncEndpoints.SynchronizeAsync(
                        filesSyncApplication,
                        circleConnections,
                        circleId,
                        token))
            .Produces<BrowserCircleFilesSyncResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status502BadGateway);
        application.MapGet(
                BrowserRoutes.Circles + "/{circleId}/viewer",
                (string circleId, CancellationToken token) =>
                    CircleFilesReadEndpoints.GetLocalViewerAsync(
                        circleApplication,
                        filesApplication,
                        circleId,
                        token))
            .Produces<BrowserCircleViewerResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
        application.MapGet(
                BrowserRoutes.Circles + "/{circleId}/files/contributions",
                (string circleId, CancellationToken token) =>
                    CircleFilesReadEndpoints.ListContributionsAsync(
                        circleApplication,
                        filesApplication,
                        circleId,
                        token))
            .Produces<CircleFilesContributionListResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
        application.MapPost(
                BrowserRoutes.Circles + "/{circleId}/files/contributions/folder-selection",
                (string circleId, CancellationToken token) =>
                    BrowserCircleFilesContributionEndpoints.SelectFolderAsync(
                        filesContributionApplication,
                        circleId,
                        token))
            .Produces<BrowserCircleFilesFolderSelectionResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
        application.MapPost(
                BrowserRoutes.Circles + "/{circleId}/files/contributions/folder-apply",
                (string circleId, ApplyBrowserCircleFilesFolderRequest request,
                    CancellationToken token) =>
                    BrowserCircleFilesContributionEndpoints.ApplyAsync(
                        filesContributionApplication,
                        circleId,
                        request,
                        token))
            .Produces<BrowserCircleFilesContributionResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
        application.MapGet(
                BrowserRoutes.Circles + "/{circleId}/files/contributions/{contributionId}/grants",
                (string circleId, string contributionId, CancellationToken token) =>
                    CircleFilesReadEndpoints.ListAccessGrantsForViewerAsync(
                        circleApplication,
                        filesApplication,
                        circleId,
                        contributionId,
                        token))
            .Produces<MemberAccessGrantListResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
        application.MapPost(
            BrowserRoutes.Circles + "/{circleId}/files/contributions/{contributionId}/grants/{grantId}/mapping/preview",
            (string circleId, string contributionId, string grantId,
                PreviewBrowserCircleFilesMemberMappingRequest request, CancellationToken token) =>
                CircleFilesMemberMappingEndpoints.PreviewBrowserAsync(
                    filesMemberMappingApplication,
                    circleConnections,
                    circleId, contributionId, grantId, request, token));
        application.MapPost(
            BrowserRoutes.Circles + "/{circleId}/files/contributions/{contributionId}/grants/{grantId}/mapping/map",
            (string circleId, string contributionId, string grantId,
                ApplyBrowserCircleFilesMemberMappingRequest request, CancellationToken token) =>
                CircleFilesMemberMappingEndpoints.MapBrowserAsync(
                    filesMemberMappingApplication,
                    circleConnections,
                    circleId, contributionId, grantId, request, token));
        application.MapPost(
            BrowserRoutes.Circles + "/{circleId}/files/contributions/{contributionId}/grants/{grantId}/mapping/inspect",
            (string circleId, string contributionId, string grantId,
                InspectBrowserCircleFilesMemberMappingRequest request, CancellationToken token) =>
                CircleFilesMemberMappingEndpoints.InspectBrowserAsync(
                    filesMemberMappingApplication,
                    circleConnections,
                    circleId, contributionId, grantId, request, token));
        application.MapPost(
            BrowserRoutes.Circles + "/{circleId}/files/contributions/{contributionId}/grants/{grantId}/mapping/unmap",
            (string circleId, string contributionId, string grantId,
                UnmapBrowserCircleFilesMemberMappingRequest request, CancellationToken token) =>
                CircleFilesMemberMappingEndpoints.UnmapBrowserAsync(
                    filesMemberMappingApplication,
                    circleConnections,
                    circleId, contributionId, grantId, request, token));
        application.MapGet(
                BrowserRoutes.Circles + "/{circleId}/messages",
                async (string circleId, CancellationToken token) =>
                {
                    if (!Guid.TryParseExact(circleId, "D", out var parsedCircleId))
                    {
                        return Results.BadRequest(
                            new ErrorResponse(
                                "invalid_circle_id",
                                "Circle ID must be a canonical UUID."));
                    }

                    var circle = await circleApplication
                        .GetCircleAsync(new CircleId(parsedCircleId), token)
                        .ConfigureAwait(false);
                    if (circle is null)
                    {
                        return Results.NotFound(
                            new ErrorResponse(
                                "circle_not_found",
                                "The requested Circle is not known to this Node."));
                    }

                    return Results.Ok(
                        await messageQueries.ListAsync(circle.Circle.Id, token)
                            .ConfigureAwait(false));
                })
            .Produces<CircleMessageListResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);
    }

    private static bool IsLoopback(IPAddress? address)
    {
        return address is not null && IPAddress.IsLoopback(address);
    }

    private static string FindWebRoot()
    {
        var packagedRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (File.Exists(Path.Combine(packagedRoot, "index.html")))
        {
            return packagedRoot;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Balls.slnx")))
            {
                return Path.Combine(directory.FullName, "web", "Balls.Web", "dist");
            }

            directory = directory.Parent;
        }

        return packagedRoot;
    }

    private static void AddSecurityHeaders(HttpResponse response)
    {
        response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        response.Headers.CacheControl = "no-store";
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        AddSecurityHeaders(context.Response);
        await context.Response.WriteAsJsonAsync(
            new ErrorResponse(code, message),
            ControlJson.Options,
            context.RequestAborted);
    }

    private static string GetProductVersion()
    {
        var informationalVersion = typeof(BrowserAdapter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return informationalVersion?.Split('+', 2)[0] ?? "unknown";
    }

    private static CircleDetailsResponse ToResponse(CircleDetails details)
    {
        return new CircleDetailsResponse(
            ToSummary(details),
            details.Members.Select(
                    member => new MemberResponse(
                        member.Id.ToString(),
                        member.DisplayName,
                        member.Role switch
                        {
                            MemberRole.Owner => "owner",
                            MemberRole.Member => "member",
                            _ => throw new InvalidOperationException("Unknown Member role."),
                        },
                        member.JoinedAtUtc))
                .ToArray(),
            details.Nodes.Select(
                    node => new CircleNodeResponse(
                        node.NodeId.ToString(),
                        node.DisplayName,
                        node.JoinedAtUtc))
                .ToArray());
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
}

internal sealed class BrowserEndpointState
{
    private Uri? baseUri;

    public Uri BaseUri => baseUri
        ?? throw new InvalidOperationException("The browser listener is not ready.");

    public string Authority => BaseUri.Authority;

    public string Origin => BaseUri.GetLeftPart(UriPartial.Authority);

    public void Initialize(Uri value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Interlocked.CompareExchange(ref baseUri, value, null) is not null)
        {
            throw new InvalidOperationException("The browser listener was initialized twice.");
        }
    }
}

internal sealed class BrowserSessionBootstrapMetadata
{
    public static BrowserSessionBootstrapMetadata Instance { get; } = new();

    private BrowserSessionBootstrapMetadata()
    {
    }
}
