using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Balls.Host;
using Balls.Platform;
using Balls.Protocol.Browser.V1;
using Balls.Protocol.Control.V1;

namespace Balls.Cli;

public static class CliApplication
{
    public static async Task<int> RunAsync(
        string[] arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (arguments.SequenceEqual(["--version"], StringComparer.Ordinal))
        {
            await standardOutput.WriteLineAsync(GetProductVersion());
            return CliExitCodes.Success;
        }

        var parseResult = ParseGlobalOptions(arguments);
        if (parseResult.Error is not null)
        {
            return await WriteUsageErrorAsync(
                standardError,
                parseResult.OutputFormat,
                parseResult.Error);
        }

        var selection = HostPlatformSelector.SelectCurrent();
        if (selection is UnsupportedHostPlatform unsupported)
        {
            await WriteErrorAsync(
                standardError,
                parseResult.OutputFormat,
                "platform_unsupported",
                unsupported.Message);
            return CliExitCodes.PlatformUnsupported;
        }

        var host = ((SupportedHostPlatform)selection).Platform;
        return await RunWithHostAsync(
            parseResult,
            host,
            standardOutput,
            standardError,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<int> RunAsync(
        string[] arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        HostPlatform host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(host);

        if (arguments.SequenceEqual(["--version"], StringComparer.Ordinal))
        {
            await standardOutput.WriteLineAsync(GetProductVersion());
            return CliExitCodes.Success;
        }

        var parseResult = ParseGlobalOptions(arguments);
        if (parseResult.Error is not null)
        {
            return await WriteUsageErrorAsync(
                standardError,
                parseResult.OutputFormat,
                parseResult.Error);
        }

        return await RunWithHostAsync(
            parseResult,
            host,
            standardOutput,
            standardError,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> RunWithHostAsync(
        GlobalOptionsParseResult parseResult,
        HostPlatform host,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        var tokens = parseResult.CommandTokens;
        var localControlEndpoint = parseResult.LocalControlEndpoint
            ?? host.Defaults.LocalControlEndpoint;

        HttpClient client;
        try
        {
            client = host.LocalControlClient.CreateClient(localControlEndpoint);
        }
        catch (ArgumentException)
        {
            return await WriteUsageErrorAsync(
                standardError,
                parseResult.OutputFormat,
                "invalid --pipe-name value.");
        }

        using (client)
            try
            {
                if (tokens.SequenceEqual(["ui"], StringComparer.Ordinal))
                {
                    return await LaunchBrowserAsync(
                        client,
                        host.SystemBrowser,
                        parseResult.OutputFormat,
                        standardOutput,
                        standardError,
                        cancellationToken).ConfigureAwait(false);
                }

                if (tokens.SequenceEqual(["status"], StringComparer.Ordinal))
                {
                    return await GetStatusAsync(
                        client,
                        parseResult.OutputFormat,
                        standardOutput,
                        standardError,
                        cancellationToken).ConfigureAwait(false);
                }

                if (tokens.Count >= 2
                    && tokens[0] == "circle"
                    && tokens[1] == "create")
                {
                    return await CreateCircleAsync(
                        client,
                        tokens,
                        parseResult.OutputFormat,
                        standardOutput,
                        standardError,
                        cancellationToken).ConfigureAwait(false);
                }

                if (tokens.Count >= 2
                    && tokens[0] == "circle"
                    && tokens[1] == "join")
                {
                    return await JoinCircleAsync(
                        client,
                        tokens,
                        parseResult.OutputFormat,
                        standardOutput,
                        standardError,
                        cancellationToken).ConfigureAwait(false);
                }

                if (tokens.SequenceEqual(["circle", "list"], StringComparer.Ordinal))
                {
                    return await ListCirclesAsync(
                        client,
                        parseResult.OutputFormat,
                        standardOutput,
                        standardError,
                        cancellationToken).ConfigureAwait(false);
                }

                if (tokens.Count >= 2
                    && tokens[0] == "invitation"
                    && tokens[1] == "create")
                {
                    return await CreateInvitationAsync(
                        client,
                        tokens,
                        parseResult.OutputFormat,
                        standardOutput,
                        standardError,
                        cancellationToken).ConfigureAwait(false);
                }

                if (tokens.Count >= 2
                    && tokens[0] == "invitation"
                    && tokens[1] == "redeem")
                {
                    return await RedeemInvitationAsync(
                        client,
                        tokens,
                        parseResult.OutputFormat,
                        standardOutput,
                        standardError,
                        cancellationToken).ConfigureAwait(false);
                }

                if (tokens.Count >= 2
                    && tokens[1] == "list"
                    && tokens[0] is "member" or "node")
                {
                    return await ListCircleParticipantsAsync(
                        client,
                        tokens,
                        parseResult.OutputFormat,
                        standardOutput,
                        standardError,
                        cancellationToken).ConfigureAwait(false);
                }

                if (tokens.Count >= 2
                    && tokens[0] == "message"
                    && tokens[1] == "send")
                {
                    return await SendMessageAsync(
                        client,
                        tokens,
                        parseResult.OutputFormat,
                        standardOutput,
                        standardError,
                        cancellationToken).ConfigureAwait(false);
                }

                if (tokens.Count >= 2
                    && tokens[0] == "message"
                    && tokens[1] == "list")
                {
                    return await ListMessagesAsync(
                        client,
                        tokens,
                        parseResult.OutputFormat,
                        standardOutput,
                        standardError,
                        cancellationToken).ConfigureAwait(false);
                }

                return await WriteUsageErrorAsync(
                    standardError,
                    parseResult.OutputFormat,
                    "unknown command.");
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                    or IOException
                    or TaskCanceledException
                    or TimeoutException)
            {
                await WriteErrorAsync(
                    standardError,
                    parseResult.OutputFormat,
                    "daemon_unavailable",
                    $"ballsd is unavailable on the selected local control {host.Defaults.LocalControlEndpointDescription}.");
                return CliExitCodes.DaemonUnavailable;
            }
    }

    private static async Task<int> LaunchBrowserAsync(
        HttpClient client,
        ISystemBrowserLauncher browser,
        CliOutputFormat outputFormat,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (outputFormat != CliOutputFormat.Text)
        {
            return await WriteUsageErrorAsync(
                error,
                outputFormat,
                "ui supports text output only.");
        }

        using var response = await client.PostAsync(
            ControlRoutes.BrowserLaunch,
            content: null,
            cancellationToken).ConfigureAwait(false);
        var result = await ReadResponseAsync<LaunchBrowserResponse>(
            response,
            outputFormat,
            error,
            cancellationToken).ConfigureAwait(false);
        if (result.Value is null)
        {
            return result.ExitCode;
        }

        if (!Uri.TryCreate(result.Value.Url, UriKind.Absolute, out var launchUri)
            || launchUri.Scheme != Uri.UriSchemeHttp
            || !IPAddress.TryParse(launchUri.Host, out var address)
            || !IPAddress.IsLoopback(address)
            || !string.IsNullOrEmpty(launchUri.UserInfo)
            || !string.IsNullOrEmpty(launchUri.Query)
            || !launchUri.Fragment.StartsWith("#launch=", StringComparison.Ordinal)
            || launchUri.Fragment.Length <= "#launch=".Length)
        {
            await WriteErrorAsync(
                error,
                outputFormat,
                "invalid_daemon_response",
                "ballsd returned an invalid browser launch address.");
            return CliExitCodes.RequestRejected;
        }

        try
        {
            browser.Open(launchUri);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or System.ComponentModel.Win32Exception)
        {
            await WriteErrorAsync(
                error,
                outputFormat,
                "browser_launch_failed",
                "The system browser could not be opened.");
            return CliExitCodes.RequestRejected;
        }

        await output.WriteLineAsync("Opened the local Balls workspace.");
        return CliExitCodes.Success;
    }

    private static async Task<int> GetStatusAsync(
        HttpClient client,
        CliOutputFormat outputFormat,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(ControlRoutes.Status, cancellationToken)
            .ConfigureAwait(false);
        var result = await ReadResponseAsync<StatusResponse>(
            response,
            outputFormat,
            error,
            cancellationToken)
            .ConfigureAwait(false);
        if (result.Value is null)
        {
            return result.ExitCode;
        }

        await WriteResultAsync(
            output,
            outputFormat,
            result.Value,
            CliOutput.RenderStatus);

        return CliExitCodes.Success;
    }

    private static async Task<int> CreateCircleAsync(
        HttpClient client,
        List<string> tokens,
        CliOutputFormat outputFormat,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!TryParseCreateCircle(tokens, out var name, out var owner, out var requestId, out var parseError))
        {
            return await WriteUsageErrorAsync(
                error,
                outputFormat,
                parseError);
        }

        using var response = await client.PostAsJsonAsync(
            ControlRoutes.Circles,
            new CreateCircleRequest(requestId, name, owner),
            ControlJson.Options,
            cancellationToken).ConfigureAwait(false);
        var result = await ReadResponseAsync<CircleDetailsResponse>(
            response,
            outputFormat,
            error,
            cancellationToken).ConfigureAwait(false);
        if (result.Value is null)
        {
            return result.ExitCode;
        }

        await WriteResultAsync(
            output,
            outputFormat,
            result.Value,
            CliOutput.RenderCreatedCircle);

        return CliExitCodes.Success;
    }

    private static async Task<int> ListCirclesAsync(
        HttpClient client,
        CliOutputFormat outputFormat,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(ControlRoutes.Circles, cancellationToken)
            .ConfigureAwait(false);
        var result = await ReadResponseAsync<CircleListResponse>(
            response,
            outputFormat,
            error,
            cancellationToken)
            .ConfigureAwait(false);
        if (result.Value is null)
        {
            return result.ExitCode;
        }

        await WriteResultAsync(
            output,
            outputFormat,
            result.Value,
            CliOutput.RenderCircles);

        return CliExitCodes.Success;
    }

    private static async Task<int> JoinCircleAsync(
        HttpClient client,
        IReadOnlyList<string> tokens,
        CliOutputFormat outputFormat,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!TryParseJoinCircle(
                tokens,
                out var invitationPath,
                out var endpoint,
                out var memberDisplayName,
                out var parseError))
        {
            return await WriteUsageErrorAsync(error, outputFormat, parseError);
        }

        var package = await ReadInvitationFileAsync(
            invitationPath,
            outputFormat,
            error,
            cancellationToken).ConfigureAwait(false);
        if (package is null)
        {
            return CliExitCodes.RequestRejected;
        }

        using var response = await client.PostAsJsonAsync(
            ControlRoutes.CircleJoin,
            new JoinCircleRequest(package, endpoint, memberDisplayName),
            ControlJson.Options,
            cancellationToken).ConfigureAwait(false);
        var result = await ReadResponseAsync<CircleDetailsResponse>(
            response,
            outputFormat,
            error,
            cancellationToken).ConfigureAwait(false);
        if (result.Value is null)
        {
            return result.ExitCode;
        }

        await WriteResultAsync(output, outputFormat, result.Value, CliOutput.RenderJoinedCircle);
        return CliExitCodes.Success;
    }

    private static async Task<int> CreateInvitationAsync(
        HttpClient client,
        IReadOnlyList<string> tokens,
        CliOutputFormat outputFormat,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!TryParseCreateInvitation(
                tokens,
                out var circleId,
                out var validForMinutes,
                out var outputPath,
                out var parseError))
        {
            return await WriteUsageErrorAsync(error, outputFormat, parseError);
        }

        using var response = await client.PostAsJsonAsync(
            ControlRoutes.CircleInvitations(circleId),
            new CreateInvitationRequest(validForMinutes),
            ControlJson.Options,
            cancellationToken).ConfigureAwait(false);
        var result = await ReadResponseAsync<CreateInvitationResponse>(
            response,
            outputFormat,
            error,
            cancellationToken).ConfigureAwait(false);
        if (result.Value is null)
        {
            return result.ExitCode;
        }

        if (outputPath is not null)
        {
            try
            {
                await using var destination = new FileStream(
                    outputPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.Asynchronous);
                await using var writer = new StreamWriter(
                    destination,
                    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    leaveOpen: false);
                await writer.WriteAsync(result.Value.Package.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is
                ArgumentException or
                IOException or
                NotSupportedException or
                UnauthorizedAccessException)
            {
                await WriteErrorAsync(
                    error,
                    outputFormat,
                    "invitation_file_write_failed",
                    "The invitation file could not be created; existing files are never overwritten.");
                return CliExitCodes.RequestRejected;
            }
        }

        await WriteResultAsync(
            output,
            outputFormat,
            result.Value,
            value => outputPath is null
                ? value.Package
                : CliOutput.RenderSavedInvitation(value, outputPath));
        return CliExitCodes.Success;
    }

    private static async Task<int> RedeemInvitationAsync(
        HttpClient client,
        IReadOnlyList<string> tokens,
        CliOutputFormat outputFormat,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (tokens.Count != 4 || tokens[2] != "--file")
        {
            return await WriteUsageErrorAsync(
                error,
                outputFormat,
                "usage: balls invitation redeem --file <path>.");
        }

        var package = await ReadInvitationFileAsync(
            tokens[3],
            outputFormat,
            error,
            cancellationToken).ConfigureAwait(false);
        if (package is null)
        {
            return CliExitCodes.RequestRejected;
        }

        using var response = await client.PostAsJsonAsync(
            ControlRoutes.Invitations + "/redeem",
            new RedeemInvitationRequest(package),
            ControlJson.Options,
            cancellationToken).ConfigureAwait(false);
        var result = await ReadResponseAsync<RedeemInvitationResponse>(
            response,
            outputFormat,
            error,
            cancellationToken).ConfigureAwait(false);
        if (result.Value is null)
        {
            return result.ExitCode;
        }

        await WriteResultAsync(
            output,
            outputFormat,
            result.Value,
            CliOutput.RenderRedeemedInvitation);
        return CliExitCodes.Success;
    }

    private static async Task<int> ListCircleParticipantsAsync(
        HttpClient client,
        List<string> tokens,
        CliOutputFormat outputFormat,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (tokens.Count != 4 || tokens[2] != "--circle")
        {
            return await WriteUsageErrorAsync(
                error,
                outputFormat,
                "usage: balls member|node list --circle <circle-id>.");
        }

        var circleId = tokens[3];

        if (tokens[0] == "member")
        {
            using var response = await client.GetAsync(
                ControlRoutes.CircleMembers(circleId),
                cancellationToken).ConfigureAwait(false);
            var result = await ReadResponseAsync<MemberListResponse>(
                response,
                outputFormat,
                error,
                cancellationToken)
                .ConfigureAwait(false);
            if (result.Value is null)
            {
                return result.ExitCode;
            }

            await WriteResultAsync(
                output,
                outputFormat,
                result.Value,
                CliOutput.RenderMembers);
        }
        else
        {
            using var response = await client.GetAsync(
                ControlRoutes.CircleNodes(circleId),
                cancellationToken).ConfigureAwait(false);
            var result = await ReadResponseAsync<NodeListResponse>(
                response,
                outputFormat,
                error,
                cancellationToken)
                .ConfigureAwait(false);
            if (result.Value is null)
            {
                return result.ExitCode;
            }

            await WriteResultAsync(
                output,
                outputFormat,
                result.Value,
                CliOutput.RenderNodes);
        }

        return CliExitCodes.Success;
    }

    private static async Task<int> SendMessageAsync(
        HttpClient client,
        IReadOnlyList<string> tokens,
        CliOutputFormat outputFormat,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!TryParseSendMessage(
                tokens,
                out var circleId,
                out var endpoint,
                out var text,
                out var messageId,
                out var parseError))
        {
            return await WriteUsageErrorAsync(error, outputFormat, parseError);
        }

        using var response = await client.PostAsJsonAsync(
            ControlRoutes.CircleMessages(circleId),
            new SendCircleMessageRequest(messageId, endpoint, text),
            ControlJson.Options,
            cancellationToken).ConfigureAwait(false);
        var result = await ReadResponseAsync<CircleMessageResponse>(
            response,
            outputFormat,
            error,
            cancellationToken).ConfigureAwait(false);
        if (result.Value is null)
        {
            return result.ExitCode;
        }

        await WriteResultAsync(output, outputFormat, result.Value, CliOutput.RenderSentMessage);
        return CliExitCodes.Success;
    }

    private static async Task<int> ListMessagesAsync(
        HttpClient client,
        IReadOnlyList<string> tokens,
        CliOutputFormat outputFormat,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (tokens.Count != 4 || tokens[2] != "--circle")
        {
            return await WriteUsageErrorAsync(
                error,
                outputFormat,
                "usage: balls message list --circle <circle-id>.");
        }

        using var response = await client.GetAsync(
            ControlRoutes.CircleMessages(tokens[3]),
            cancellationToken).ConfigureAwait(false);
        var result = await ReadResponseAsync<CircleMessageListResponse>(
            response,
            outputFormat,
            error,
            cancellationToken).ConfigureAwait(false);
        if (result.Value is null)
        {
            return result.ExitCode;
        }

        await WriteResultAsync(output, outputFormat, result.Value, CliOutput.RenderMessages);
        return CliExitCodes.Success;
    }

    private static async Task<ResponseResult<T>> ReadResponseAsync<T>(
        HttpResponseMessage response,
        CliOutputFormat outputFormat,
        TextWriter error,
        CancellationToken cancellationToken)
        where T : class
    {
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(
                ControlJson.Options,
                cancellationToken).ConfigureAwait(false);
            if (value is null)
            {
                await WriteErrorAsync(
                    error,
                    outputFormat,
                    "invalid_daemon_response",
                    "ballsd returned an empty response.");
                return new ResponseResult<T>(null, CliExitCodes.RequestRejected);
            }

            return new ResponseResult<T>(value, CliExitCodes.Success);
        }

        ErrorResponse? apiError = null;
        try
        {
            apiError = await response.Content.ReadFromJsonAsync<ErrorResponse>(
                ControlJson.Options,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
        }

        await WriteErrorAsync(
            error,
            outputFormat,
            apiError?.Code ?? "request_rejected",
            apiError?.Message ?? $"ballsd rejected the request ({(int)response.StatusCode}).",
            includeCodeInText: apiError is not null);
        return new ResponseResult<T>(null, CliExitCodes.RequestRejected);
    }

    private static GlobalOptionsParseResult ParseGlobalOptions(string[] arguments)
    {
        var outputFormat = CliOutputFormat.Text;
        string? localControlEndpoint = null;
        var outputSeen = false;
        var endpointSeen = false;
        var index = 0;

        while (index < arguments.Length && arguments[index].StartsWith("--", StringComparison.Ordinal))
        {
            var option = arguments[index];
            if (option is not ("--pipe-name" or "--output"))
            {
                return GlobalOptionsParseResult.Failure(
                    outputFormat,
                    $"unknown global option '{option}'.");
            }

            if (index + 1 >= arguments.Length
                || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return GlobalOptionsParseResult.Failure(
                    outputFormat,
                    $"{option} requires a value.");
            }

            var value = arguments[index + 1];
            if (option == "--pipe-name")
            {
                if (endpointSeen)
                {
                    return GlobalOptionsParseResult.Failure(
                        outputFormat,
                        "--pipe-name may be specified only once.");
                }

                endpointSeen = true;
                localControlEndpoint = value;
            }
            else
            {
                if (outputSeen)
                {
                    return GlobalOptionsParseResult.Failure(
                        outputFormat,
                        "--output may be specified only once.");
                }

                outputSeen = true;
                if (value is not ("text" or "json"))
                {
                    return GlobalOptionsParseResult.Failure(
                        outputFormat,
                        "--output must be either 'text' or 'json'.");
                }

                outputFormat = value == "json" ? CliOutputFormat.Json : CliOutputFormat.Text;
            }

            index += 2;
        }

        return GlobalOptionsParseResult.Success(
            outputFormat,
            localControlEndpoint,
            arguments[index..].ToList());
    }

    private static bool TryParseCreateCircle(
        IReadOnlyList<string> tokens,
        out string name,
        out string owner,
        out string requestId,
        out string error)
    {
        name = string.Empty;
        owner = string.Empty;
        requestId = Guid.CreateVersion7().ToString("D");
        error = "usage: balls circle create <name> --owner <display-name> [--request-id <uuid>].";

        if (tokens.Count < 5
            || tokens[0] != "circle"
            || tokens[1] != "create"
            || tokens[2].StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        name = tokens[2];
        var ownerSeen = false;
        var requestIdSeen = false;
        for (var index = 3; index < tokens.Count; index += 2)
        {
            if (index + 1 >= tokens.Count
                || tokens[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"{tokens[index]} requires a value.";
                return false;
            }

            switch (tokens[index])
            {
                case "--owner" when !ownerSeen:
                    ownerSeen = true;
                    owner = tokens[index + 1];
                    break;
                case "--request-id" when !requestIdSeen:
                    requestIdSeen = true;
                    requestId = tokens[index + 1];
                    break;
                case "--owner":
                    error = "--owner may be specified only once.";
                    return false;
                case "--request-id":
                    error = "--request-id may be specified only once.";
                    return false;
                default:
                    error = $"unknown circle create option '{tokens[index]}'.";
                    return false;
            }
        }

        if (!ownerSeen)
        {
            error = "circle create requires --owner <display-name>.";
            return false;
        }

        return true;
    }

    private static bool TryParseCreateInvitation(
        IReadOnlyList<string> tokens,
        out string circleId,
        out int validForMinutes,
        out string? outputPath,
        out string error)
    {
        circleId = string.Empty;
        validForMinutes = InvitationApplicationDefaults.DefaultValidityMinutes;
        outputPath = null;
        error = "usage: balls invitation create --circle <circle-id> [--valid-for-minutes <minutes>] [--out <path>].";
        if (tokens.Count < 4 || tokens.Count % 2 != 0)
        {
            return false;
        }

        var circleSeen = false;
        var validitySeen = false;
        var outputSeen = false;
        for (var index = 2; index < tokens.Count; index += 2)
        {
            if (index + 1 >= tokens.Count || tokens[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"{tokens[index]} requires a value.";
                return false;
            }

            switch (tokens[index])
            {
                case "--circle" when !circleSeen:
                    circleSeen = true;
                    circleId = tokens[index + 1];
                    break;
                case "--valid-for-minutes" when !validitySeen:
                    validitySeen = true;
                    if (!int.TryParse(
                            tokens[index + 1],
                            System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out validForMinutes))
                    {
                        error = "--valid-for-minutes requires a whole number.";
                        return false;
                    }

                    break;
                case "--out" when !outputSeen:
                    outputSeen = true;
                    outputPath = tokens[index + 1];
                    break;
                default:
                    error = $"unknown or repeated invitation create option '{tokens[index]}'.";
                    return false;
            }
        }

        if (!circleSeen)
        {
            error = "invitation create requires --circle <circle-id>.";
            return false;
        }

        return true;
    }

    private static bool TryParseJoinCircle(
        IReadOnlyList<string> tokens,
        out string invitationPath,
        out string endpoint,
        out string memberDisplayName,
        out string error)
    {
        invitationPath = string.Empty;
        endpoint = string.Empty;
        memberDisplayName = string.Empty;
        error = "usage: balls circle join --file <path> --endpoint <private-ip:port> --member <display-name>.";
        if (tokens.Count != 8)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 2; index < tokens.Count; index += 2)
        {
            if (index + 1 >= tokens.Count
                || tokens[index + 1].StartsWith("--", StringComparison.Ordinal)
                || !seen.Add(tokens[index]))
            {
                return false;
            }

            switch (tokens[index])
            {
                case "--file":
                    invitationPath = tokens[index + 1];
                    break;
                case "--endpoint":
                    endpoint = tokens[index + 1];
                    break;
                case "--member":
                    memberDisplayName = tokens[index + 1];
                    break;
                default:
                    return false;
            }
        }

        return invitationPath.Length > 0 && endpoint.Length > 0 && memberDisplayName.Length > 0;
    }

    private static bool TryParseSendMessage(
        IReadOnlyList<string> tokens,
        out string circleId,
        out string endpoint,
        out string text,
        out string messageId,
        out string error)
    {
        circleId = string.Empty;
        endpoint = string.Empty;
        text = string.Empty;
        messageId = Guid.CreateVersion7().ToString("D");
        error = "usage: balls message send --circle <circle-id> --endpoint <private-ip:port> --text <text> [--message-id <uuid>].";
        if (tokens.Count is not (8 or 10) || tokens.Count % 2 != 0)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 2; index < tokens.Count; index += 2)
        {
            if (index + 1 >= tokens.Count
                || tokens[index + 1].StartsWith("--", StringComparison.Ordinal)
                || !seen.Add(tokens[index]))
            {
                return false;
            }

            switch (tokens[index])
            {
                case "--circle":
                    circleId = tokens[index + 1];
                    break;
                case "--endpoint":
                    endpoint = tokens[index + 1];
                    break;
                case "--text":
                    text = tokens[index + 1];
                    break;
                case "--message-id":
                    messageId = tokens[index + 1];
                    break;
                default:
                    return false;
            }
        }

        return circleId.Length > 0 && endpoint.Length > 0 && text.Length > 0;
    }

    private static async Task<string?> ReadInvitationFileAsync(
        string path,
        CliOutputFormat outputFormat,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var source = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (source.Length is 0 or > Balls.Protocol.Remote.V1.InvitationPackageCodec.MaximumEncodedLength)
            {
                throw new InvalidDataException();
            }

            using var reader = new StreamReader(
                source,
                new System.Text.UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: false);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            ArgumentException or DecoderFallbackException or IOException or
            NotSupportedException or UnauthorizedAccessException)
        {
            await WriteErrorAsync(
                error,
                outputFormat,
                "invitation_file_invalid",
                "The invitation file is missing, unreadable, or exceeds the 16 KiB limit.");
            return null;
        }
    }

    private static async Task WriteResultAsync<T>(
        TextWriter output,
        CliOutputFormat outputFormat,
        T value,
        Func<T, string> renderText)
    {
        var rendered = outputFormat == CliOutputFormat.Json
            ? CliOutput.SerializeResult(value)
            : renderText(value);
        if (rendered.Length > 0)
        {
            await output.WriteLineAsync(rendered);
        }
    }

    private static async Task WriteErrorAsync(
        TextWriter error,
        CliOutputFormat outputFormat,
        string code,
        string message,
        bool includeCodeInText = false)
    {
        await error.WriteLineAsync(
            outputFormat == CliOutputFormat.Json
                ? CliOutput.SerializeError(code, message)
                : $"balls: {message}{(includeCodeInText ? $" ({code})" : string.Empty)}");
    }

    private static async Task<int> WriteUsageErrorAsync(
        TextWriter error,
        CliOutputFormat outputFormat,
        string message)
    {
        await WriteErrorAsync(error, outputFormat, "usage_error", message);
        if (outputFormat == CliOutputFormat.Text)
        {
            await error.WriteLineAsync(
                "commands: ui | status | circle create | circle join | circle list | member list | node list | message send | message list | invitation create | invitation redeem");
        }

        return CliExitCodes.UsageError;
    }

    private static string GetProductVersion()
    {
        return typeof(CliApplication).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+', 2)[0]
            ?? "unknown";
    }

    private sealed record ResponseResult<T>(T? Value, int ExitCode)
        where T : class;

    private sealed record GlobalOptionsParseResult(
        CliOutputFormat OutputFormat,
        string? LocalControlEndpoint,
        List<string> CommandTokens,
        string? Error)
    {
        internal static GlobalOptionsParseResult Success(
            CliOutputFormat outputFormat,
            string? localControlEndpoint,
            List<string> commandTokens)
        {
            return new GlobalOptionsParseResult(
                outputFormat,
                localControlEndpoint,
                commandTokens,
                null);
        }

        internal static GlobalOptionsParseResult Failure(
            CliOutputFormat outputFormat,
            string error)
        {
            return new GlobalOptionsParseResult(outputFormat, null, [], error);
        }
    }

    private static class InvitationApplicationDefaults
    {
        internal const int DefaultValidityMinutes = 60;
    }
}
