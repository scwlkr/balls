using System.Net;
using System.Net.Sockets;
using Balls.Core;
using Balls.Protocol.Remote.V1;
using Balls.Transport.Lan;

namespace Balls.Daemon;

internal sealed record BrowserCircleConnection(
    RemoteTransportAddress AdmissionAddress,
    RemoteTransportAddress SyncAddress,
    string FilesHost);

internal static class BrowserCircleConnections
{
    internal static BrowserCircleConnection ParseInvitation(
        string provider,
        string admissionEndpoint,
        string syncEndpoint)
    {
        var admission = ParsePrivateAddress(provider, admissionEndpoint);
        var sync = ParsePrivateAddress(provider, syncEndpoint);
        return new BrowserCircleConnection(
            new RemoteTransportAddress(provider, admission.ToString()),
            new RemoteTransportAddress(provider, sync.ToString()),
            admission.Address.ToString());
    }

    internal static async Task<BrowserCircleConnection> LoadAsync(
        IAdmissionStateStore state,
        CircleId circleId,
        CancellationToken cancellationToken)
    {
        var connection = await state.GetCircleConnectionAsync(circleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LocalStateException(
                "circle_connection_missing",
                "This Circle does not have a saved private connection. Ask the Circle owner for a new invitation.");
        if (connection.Version != 1)
        {
            throw InvalidSavedConnection();
        }

        try
        {
            return ParseInvitation(
                connection.Provider,
                connection.AdmissionEndpoint,
                connection.SyncEndpoint);
        }
        catch (InputValidationException)
        {
            throw InvalidSavedConnection();
        }
    }

    private static IPEndPoint ParsePrivateAddress(string provider, string endpoint)
    {
        if (!string.Equals(provider, LanTcpEndpoint.ProviderName, StringComparison.Ordinal))
        {
            throw new InputValidationException(
                "unsupported_circle_connection",
                "This invitation uses a private connection provider that Balls does not support.");
        }

        try
        {
            var parsed = LanTcpEndpoint.Parse(new RemoteTransportAddress(provider, endpoint));
            if (parsed.Address.AddressFamily != AddressFamily.InterNetwork
                || !LanTcpEndpoint.IsPrivateIPv4(parsed.Address))
            {
                throw new ArgumentException("The invitation endpoint is not private IPv4.");
            }

            return parsed;
        }
        catch (ArgumentException)
        {
            throw new InputValidationException(
                "invalid_circle_connection",
                "This invitation does not contain a valid private Circle connection.");
        }
    }

    private static LocalStateException InvalidSavedConnection() =>
        new(
            "invalid_circle_connection",
            "The saved private Circle connection is invalid. Ask the Circle owner for a new invitation.");
}
