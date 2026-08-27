using System.Net;
using System.Reflection;
using Balls.Core;
using Balls.Host;
using Balls.Protocol.Remote.V1;
using Balls.Transport.Lan;

namespace Balls.Daemon;

public static class DaemonExitCodes
{
    public const int Success = 0;
    public const int UsageError = 2;
    public const int StartupFailure = 4;
    public const int PlatformUnsupported = 5;
}

public static class DaemonCommand
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
            return DaemonExitCodes.Success;
        }

        if (arguments.SequenceEqual(["--help"], StringComparer.Ordinal))
        {
            await WriteUsageAsync(standardOutput);
            return DaemonExitCodes.Success;
        }

        var selection = HostPlatformSelector.SelectCurrent();
        if (selection is UnsupportedHostPlatform unsupported)
        {
            await standardError.WriteLineAsync($"ballsd: {unsupported.Message}");
            return DaemonExitCodes.PlatformUnsupported;
        }

        var supported = (SupportedHostPlatform)selection;
        var host = supported.Platform;
        var tokens = arguments.ToList();
        var dataDirectory = host.Defaults.DataDirectory;
        var localControlEndpoint = host.Defaults.LocalControlEndpoint;
        var nodeName = host.Defaults.NodeDisplayName;
        string? admissionListenEndpoint = null;
        string? messageListenEndpoint = null;
        string? advertisedPrivateAddress = null;
        var automaticPrivateListeners = false;

        if (!TryApplyOption(tokens, "--data-directory", ref dataDirectory, out var error)
            || !TryApplyOption(tokens, "--pipe-name", ref localControlEndpoint, out error)
            || !TryApplyOption(tokens, "--node-name", ref nodeName, out error)
            || !TryApplyOptionalOption(
                tokens,
                "--admission-listen",
                ref admissionListenEndpoint,
                out error)
            || !TryApplyOptionalOption(
                tokens,
                "--message-listen",
                ref messageListenEndpoint,
                out error)
            || !TryApplyOptionalOption(
                tokens,
                "--advertised-private-address",
                ref advertisedPrivateAddress,
                out error)
            || !TryApplyFlag(
                tokens,
                "--automatic-private-listeners",
                ref automaticPrivateListeners,
                out error))
        {
            await standardError.WriteLineAsync($"ballsd: {error}");
            await WriteUsageAsync(standardError);
            return DaemonExitCodes.UsageError;
        }

        if (tokens.Count != 0)
        {
            await standardError.WriteLineAsync($"ballsd: unknown argument '{tokens[0]}'.");
            await WriteUsageAsync(standardError);
            return DaemonExitCodes.UsageError;
        }

        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            await standardError.WriteLineAsync("ballsd: --data-directory requires a non-blank value.");
            await WriteUsageAsync(standardError);
            return DaemonExitCodes.UsageError;
        }

        if (string.IsNullOrWhiteSpace(nodeName))
        {
            await standardError.WriteLineAsync("ballsd: --node-name requires a non-blank value.");
            await WriteUsageAsync(standardError);
            return DaemonExitCodes.UsageError;
        }
        if (nodeName.Trim().Length > 100)
        {
            await standardError.WriteLineAsync("ballsd: --node-name cannot exceed 100 characters.");
            await WriteUsageAsync(standardError);
            return DaemonExitCodes.UsageError;
        }

        try
        {
            host.LocalControlServer.ValidateEndpoint(localControlEndpoint);
        }
        catch (ArgumentException)
        {
            await standardError.WriteLineAsync("ballsd: invalid --pipe-name value.");
            await WriteUsageAsync(standardError);
            return DaemonExitCodes.UsageError;
        }

        if (admissionListenEndpoint is not null)
        {
            try
            {
                _ = LanTcpEndpoint.Parse(
                    new RemoteTransportAddress(
                        LanTcpEndpoint.ProviderName,
                        admissionListenEndpoint));
            }
            catch (ArgumentException)
            {
                await standardError.WriteLineAsync(
                    "ballsd: invalid --admission-listen value.");
                await WriteUsageAsync(standardError);
                return DaemonExitCodes.UsageError;
            }
        }

        if (messageListenEndpoint is not null)
        {
            try
            {
                _ = LanTcpEndpoint.Parse(
                    new RemoteTransportAddress(
                        LanTcpEndpoint.ProviderName,
                        messageListenEndpoint));
            }
            catch (ArgumentException)
            {
                await standardError.WriteLineAsync(
                    "ballsd: invalid --message-listen value.");
                await WriteUsageAsync(standardError);
                return DaemonExitCodes.UsageError;
            }
        }

        if (advertisedPrivateAddress is not null
            && (!automaticPrivateListeners
                || !IPAddress.TryParse(advertisedPrivateAddress, out var parsedAdvertisedAddress)
                || !LanTcpEndpoint.IsPrivateIPv4(parsedAdvertisedAddress)))
        {
            await standardError.WriteLineAsync(
                "ballsd: invalid --advertised-private-address value.");
            await WriteUsageAsync(standardError);
            return DaemonExitCodes.UsageError;
        }

        try
        {
            await using var daemon = await DaemonHost.StartAsync(
                new DaemonOptions(
                    dataDirectory,
                    localControlEndpoint,
                    nodeName,
                    admissionListenEndpoint,
                    messageListenEndpoint,
                    automaticPrivateListeners,
                    advertisedPrivateAddress),
                host,
                supported.PrivateMaterialProtector,
                cancellationToken).ConfigureAwait(false);
            await standardOutput.WriteLineAsync(
                $"ballsd ready on {host.Defaults.LocalControlListenerDescription} {localControlEndpoint}.");
            if (daemon.AdmissionAddress is not null)
            {
                await standardOutput.WriteLineAsync(
                    $"ballsd admission ready on {daemon.AdmissionAddress.Value}.");
            }
            if (daemon.MessageAddress is not null)
            {
                await standardOutput.WriteLineAsync(
                    $"ballsd messages ready on {daemon.MessageAddress.Value}.");
            }
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            return DaemonExitCodes.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DaemonExitCodes.Success;
        }
        catch (Exception exception) when (
            exception is DataDirectoryInUseException
                or LocalStateException
                or InputValidationException
                or UnauthorizedAccessException
                or IOException)
        {
            await standardError.WriteLineAsync($"ballsd: {exception.Message}");
            return DaemonExitCodes.StartupFailure;
        }
    }

    private static bool TryApplyOption(
        List<string> tokens,
        string option,
        ref string destination,
        out string? error)
    {
        var index = tokens.FindIndex(token => string.Equals(token, option, StringComparison.Ordinal));
        if (index < 0)
        {
            error = null;
            return true;
        }

        if (index == tokens.Count - 1)
        {
            error = $"{option} requires a value.";
            return false;
        }

        destination = tokens[index + 1];
        tokens.RemoveRange(index, 2);
        error = null;
        return true;
    }

    private static bool TryApplyOptionalOption(
        List<string> tokens,
        string option,
        ref string? destination,
        out string? error)
    {
        var index = tokens.FindIndex(token => string.Equals(token, option, StringComparison.Ordinal));
        if (index < 0)
        {
            error = null;
            return true;
        }

        if (index == tokens.Count - 1)
        {
            error = $"{option} requires a value.";
            return false;
        }

        destination = tokens[index + 1];
        tokens.RemoveRange(index, 2);
        error = null;
        return true;
    }

    private static bool TryApplyFlag(
        List<string> tokens,
        string option,
        ref bool destination,
        out string? error)
    {
        var index = tokens.FindIndex(token => string.Equals(token, option, StringComparison.Ordinal));
        if (index < 0)
        {
            error = null;
            return true;
        }

        destination = true;
        tokens.RemoveAt(index);
        error = null;
        return true;
    }

    private static Task WriteUsageAsync(TextWriter writer)
    {
        return writer.WriteLineAsync(
            "usage: ballsd [--data-directory <path>] [--pipe-name <name>] [--node-name <name>] [--admission-listen <private-ip:port>] [--message-listen <private-ip:port>] [--automatic-private-listeners] [--advertised-private-address <private-ip>]");
    }

    private static string GetProductVersion()
    {
        return typeof(DaemonCommand).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+', 2)[0]
            ?? "unknown";
    }
}
