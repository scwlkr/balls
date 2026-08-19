using System.Text.Json;
using Balls.Protocol.Control.V1;

namespace Balls.Cli;

internal enum CliOutputFormat
{
    Text,
    Json,
}

internal static class CliOutput
{
    internal const int OutputVersion = 1;

    internal static string SerializeResult<T>(T result)
    {
        return JsonSerializer.Serialize(
            new CliResultEnvelope<T>(OutputVersion, result),
            ControlJson.Options);
    }

    internal static string SerializeError(string code, string message)
    {
        return JsonSerializer.Serialize(
            new CliErrorEnvelope(OutputVersion, new CliError(code, message)),
            ControlJson.Options);
    }

    internal static string RenderStatus(StatusResponse response)
    {
        return string.Join(
            Environment.NewLine,
            $"Node: {response.Node.DisplayName}",
            $"Node ID: {response.Node.Id}",
            $"Control protocol: v{response.ProtocolVersion}");
    }

    internal static string RenderCreatedCircle(CircleDetailsResponse response)
    {
        return string.Join(
            Environment.NewLine,
            $"Created Circle: {response.Circle.Name}",
            $"Circle ID: {response.Circle.Id}");
    }

    internal static string RenderCircles(CircleListResponse response)
    {
        return response.Circles.Count == 0
            ? "No Circles."
            : string.Join(
                Environment.NewLine,
                response.Circles.Select(circle =>
                    $"{circle.Id}\t{circle.Name}\t{circle.MemberCount} member(s)\t{circle.NodeCount} node(s)"));
    }

    internal static string RenderMembers(MemberListResponse response)
    {
        return string.Join(
            Environment.NewLine,
            response.Members.Select(member =>
                $"{member.Id}\t{member.DisplayName}\t{member.Role}"));
    }

    internal static string RenderNodes(NodeListResponse response)
    {
        return string.Join(
            Environment.NewLine,
            response.Nodes.Select(node => $"{node.Id}\t{node.DisplayName}"));
    }

    private sealed record CliResultEnvelope<T>(int OutputVersion, T Result);

    private sealed record CliErrorEnvelope(int OutputVersion, CliError Error);

    private sealed record CliError(string Code, string Message);
}
