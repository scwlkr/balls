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

    internal static string RenderJoinedCircle(CircleDetailsResponse response)
    {
        return string.Join(
            Environment.NewLine,
            $"Joined Circle: {response.Circle.Name}",
            $"Circle ID: {response.Circle.Id}",
            $"Members: {response.Circle.MemberCount}",
            $"Nodes: {response.Circle.NodeCount}");
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

    internal static string RenderSentMessage(CircleMessageResponse response) =>
        string.Join(
            Environment.NewLine,
            $"Sent message: {response.Id}",
            $"Sequence: {response.Sequence}",
            response.Text);

    internal static string RenderMessages(CircleMessageListResponse response) =>
        response.Messages.Count == 0
            ? "No messages."
            : string.Join(
                Environment.NewLine,
                response.Messages.Select(message =>
                    $"{message.Sequence}\t{message.AuthorMemberId}\t{message.Text}"));

    internal static string RenderCreatedFilesContribution(
        CircleFilesContributionResponse response) =>
        string.Join(
            Environment.NewLine,
            $"Defined contribution: {response.DisplayName}",
            $"Contribution ID: {response.Id}",
            $"Provider ID: {response.Provider.Id}");

    internal static string RenderFilesContributions(
        CircleFilesContributionListResponse response) =>
        response.Contributions.Count == 0
            ? "No Circle Files contributions."
            : string.Join(
                Environment.NewLine,
                response.Contributions.Select(value =>
                    $"{value.Id}\t{value.DisplayName}\t{value.Lifecycle}\tgeneration {value.Generation}"));

    internal static string RenderCreatedFilesGrant(MemberAccessGrantResponse response) =>
        string.Join(
            Environment.NewLine,
            $"Defined Access Grant: {response.Id}",
            $"Member ID: {response.MemberId}",
            $"Access: {response.Access}");

    internal static string RenderFilesGrants(MemberAccessGrantListResponse response) =>
        response.Grants.Count == 0
            ? "No Member Access Grants."
            : string.Join(
                Environment.NewLine,
                response.Grants.Select(value =>
                    $"{value.Id}\t{value.MemberId}\t{value.Access}\t{value.Lifecycle}\tgeneration {value.Generation}"));

    internal static string RenderSavedInvitation(
        CreateInvitationResponse response,
        string path) =>
        string.Join(
            Environment.NewLine,
            $"Created invitation: {response.InvitationId}",
            $"Expires: {response.ExpiresAtUtc:O}",
            $"Saved: {Path.GetFullPath(path)}");

    internal static string RenderRedeemedInvitation(RedeemInvitationResponse response) =>
        string.Join(
            Environment.NewLine,
            "Invitation accepted.",
            $"Circle ID: {response.CircleId}",
            $"Redemption ID: {response.RedemptionId}");

    private sealed record CliResultEnvelope<T>(int OutputVersion, T Result);

    private sealed record CliErrorEnvelope(int OutputVersion, CliError Error);

    private sealed record CliError(string Code, string Message);
}
