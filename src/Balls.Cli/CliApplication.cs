using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Balls.Platform.Windows;
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

        if (!OperatingSystem.IsWindows())
        {
            await standardError.WriteLineAsync(
                "balls: the Phase 1 local control transport currently requires Windows.");
            return CliExitCodes.PlatformUnsupported;
        }

        var tokens = arguments.ToList();
        string pipeName;
        if (TryTakeOption(tokens, "--pipe-name", out var requestedPipeName))
        {
            if (requestedPipeName is null)
            {
                return await WriteUsageErrorAsync(
                    standardError,
                    "--pipe-name requires a value.");
            }

            pipeName = requestedPipeName;
        }
        else
        {
            pipeName = WindowsNamedPipeDefaults.GetCurrentUserPipeName();
        }

        var outputFormat = "text";
        if (TryTakeOption(tokens, "--output", out var requestedOutput))
        {
            if (requestedOutput is not ("text" or "json"))
            {
                return await WriteUsageErrorAsync(
                    standardError,
                    "--output must be either 'text' or 'json'.");
            }

            outputFormat = requestedOutput;
        }

        HttpClient client;
        try
        {
            client = WindowsNamedPipeHttpClient.Create(pipeName);
        }
        catch (ArgumentException)
        {
            return await WriteUsageErrorAsync(standardError, "invalid --pipe-name value.");
        }

        using (client)
            try
            {
                if (tokens.SequenceEqual(["status"], StringComparer.Ordinal))
                {
                    return await GetStatusAsync(
                        client,
                        outputFormat,
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
                        outputFormat,
                        standardOutput,
                        standardError,
                        cancellationToken).ConfigureAwait(false);
                }

                if (tokens.SequenceEqual(["circle", "list"], StringComparer.Ordinal))
                {
                    return await ListCirclesAsync(
                        client,
                        outputFormat,
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
                        outputFormat,
                        standardOutput,
                        standardError,
                        cancellationToken).ConfigureAwait(false);
                }

                return await WriteUsageErrorAsync(standardError, "unknown command.");
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                    or IOException
                    or TaskCanceledException
                    or TimeoutException)
            {
                await standardError.WriteLineAsync(
                    "balls: ballsd is unavailable on the selected local control pipe.");
                return CliExitCodes.DaemonUnavailable;
            }
    }

    private static async Task<int> GetStatusAsync(
        HttpClient client,
        string outputFormat,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(ControlRoutes.Status, cancellationToken)
            .ConfigureAwait(false);
        var result = await ReadResponseAsync<StatusResponse>(response, error, cancellationToken)
            .ConfigureAwait(false);
        if (result.Value is null)
        {
            return result.ExitCode;
        }

        if (outputFormat == "json")
        {
            await WriteJsonAsync(output, result.Value);
        }
        else
        {
            await output.WriteLineAsync($"Node: {result.Value.Node.DisplayName}");
            await output.WriteLineAsync($"Node ID: {result.Value.Node.Id}");
            await output.WriteLineAsync($"Control protocol: v{result.Value.ProtocolVersion}");
        }

        return CliExitCodes.Success;
    }

    private static async Task<int> CreateCircleAsync(
        HttpClient client,
        List<string> tokens,
        string outputFormat,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!TryTakeOption(tokens, "--owner", out var owner) || owner is null)
        {
            return await WriteUsageErrorAsync(error, "circle create requires --owner <display-name>.");
        }

        var requestId = Guid.CreateVersion7().ToString("D");
        if (TryTakeOption(tokens, "--request-id", out var requestedId))
        {
            if (requestedId is null)
            {
                return await WriteUsageErrorAsync(error, "--request-id requires a value.");
            }

            requestId = requestedId;
        }

        if (tokens.Count != 3 || tokens[0] != "circle" || tokens[1] != "create")
        {
            return await WriteUsageErrorAsync(
                error,
                "usage: balls circle create <name> --owner <display-name>.");
        }

        using var response = await client.PostAsJsonAsync(
            ControlRoutes.Circles,
            new CreateCircleRequest(requestId, tokens[2], owner),
            ControlJson.Options,
            cancellationToken).ConfigureAwait(false);
        var result = await ReadResponseAsync<CircleDetailsResponse>(
            response,
            error,
            cancellationToken).ConfigureAwait(false);
        if (result.Value is null)
        {
            return result.ExitCode;
        }

        if (outputFormat == "json")
        {
            await WriteJsonAsync(output, result.Value);
        }
        else
        {
            await output.WriteLineAsync($"Created Circle: {result.Value.Circle.Name}");
            await output.WriteLineAsync($"Circle ID: {result.Value.Circle.Id}");
        }

        return CliExitCodes.Success;
    }

    private static async Task<int> ListCirclesAsync(
        HttpClient client,
        string outputFormat,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(ControlRoutes.Circles, cancellationToken)
            .ConfigureAwait(false);
        var result = await ReadResponseAsync<CircleListResponse>(response, error, cancellationToken)
            .ConfigureAwait(false);
        if (result.Value is null)
        {
            return result.ExitCode;
        }

        if (outputFormat == "json")
        {
            await WriteJsonAsync(output, result.Value);
        }
        else if (result.Value.Circles.Count == 0)
        {
            await output.WriteLineAsync("No Circles.");
        }
        else
        {
            foreach (var circle in result.Value.Circles)
            {
                await output.WriteLineAsync(
                    $"{circle.Id}\t{circle.Name}\t{circle.MemberCount} member(s)\t{circle.NodeCount} node(s)");
            }
        }

        return CliExitCodes.Success;
    }

    private static async Task<int> ListCircleParticipantsAsync(
        HttpClient client,
        List<string> tokens,
        string outputFormat,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!TryTakeOption(tokens, "--circle", out var circleId) || circleId is null)
        {
            return await WriteUsageErrorAsync(error, "list requires --circle <circle-id>.");
        }

        if (tokens.Count != 2)
        {
            return await WriteUsageErrorAsync(error, "unknown list arguments.");
        }

        if (tokens[0] == "member")
        {
            using var response = await client.GetAsync(
                ControlRoutes.CircleMembers(circleId),
                cancellationToken).ConfigureAwait(false);
            var result = await ReadResponseAsync<MemberListResponse>(response, error, cancellationToken)
                .ConfigureAwait(false);
            if (result.Value is null)
            {
                return result.ExitCode;
            }

            if (outputFormat == "json")
            {
                await WriteJsonAsync(output, result.Value);
            }
            else
            {
                foreach (var member in result.Value.Members)
                {
                    await output.WriteLineAsync(
                        $"{member.Id}\t{member.DisplayName}\t{member.Role}");
                }
            }
        }
        else
        {
            using var response = await client.GetAsync(
                ControlRoutes.CircleNodes(circleId),
                cancellationToken).ConfigureAwait(false);
            var result = await ReadResponseAsync<NodeListResponse>(response, error, cancellationToken)
                .ConfigureAwait(false);
            if (result.Value is null)
            {
                return result.ExitCode;
            }

            if (outputFormat == "json")
            {
                await WriteJsonAsync(output, result.Value);
            }
            else
            {
                foreach (var node in result.Value.Nodes)
                {
                    await output.WriteLineAsync($"{node.Id}\t{node.DisplayName}");
                }
            }
        }

        return CliExitCodes.Success;
    }

    private static async Task<ResponseResult<T>> ReadResponseAsync<T>(
        HttpResponseMessage response,
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
                await error.WriteLineAsync("balls: ballsd returned an empty response.");
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

        await error.WriteLineAsync(
            apiError is null
                ? $"balls: ballsd rejected the request ({(int)response.StatusCode})."
                : $"balls: {apiError.Message} ({apiError.Code})");
        return new ResponseResult<T>(null, CliExitCodes.RequestRejected);
    }

    private static bool TryTakeOption(
        List<string> tokens,
        string option,
        out string? value)
    {
        var index = tokens.FindIndex(token => string.Equals(token, option, StringComparison.Ordinal));
        if (index < 0)
        {
            value = null;
            return false;
        }

        if (index == tokens.Count - 1)
        {
            value = null;
            tokens.RemoveAt(index);
            return true;
        }

        value = tokens[index + 1];
        tokens.RemoveRange(index, 2);
        return true;
    }

    private static async Task WriteJsonAsync<T>(TextWriter output, T value)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(value, ControlJson.Options));
    }

    private static async Task<int> WriteUsageErrorAsync(TextWriter error, string message)
    {
        await error.WriteLineAsync($"balls: {message}");
        await error.WriteLineAsync(
            "commands: status | circle create | circle list | member list | node list");
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
}
