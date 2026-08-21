using System.ComponentModel;
using System.Security;
using System.Text.Json;
using Balls.Platform;

namespace Balls.Platform.Windows;

public sealed class WindowsSmbReadinessInspector : ICircleFilesReadinessInspector
{
    private readonly IWindowsPowerShellJsonSource source;

    public WindowsSmbReadinessInspector()
        : this(new StaticWindowsPowerShellJsonSource())
    {
    }

    internal WindowsSmbReadinessInspector(IWindowsPowerShellJsonSource source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public async ValueTask<CircleFilesReadinessReport> InspectAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await source.QueryAsync(
                WindowsPowerShellQuery.SmbReadiness,
                cancellationToken).ConfigureAwait(false);
            return Evaluate(WindowsSmbReadinessJson.Parse(json));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedInspectionFailure(exception))
        {
            return InspectionFailed();
        }
    }

    private static CircleFilesReadinessReport Evaluate(WindowsSmbReadinessObservation observation)
    {
        CircleFilesReadinessCheck[] checks =
        [
            EvaluatePlatform(observation.System),
            EvaluateSmbServer(observation.Services, observation.SmbServer),
            EvaluateDialect(observation.SmbServer),
            EvaluateSmb1(observation.SmbServer),
            EvaluateGuestAccess(observation.SmbClient),
            EvaluateSigning(observation.SmbServer),
            EvaluateEncryption(observation.SmbServer),
            EvaluatePrivateNetwork(observation.Network),
            EvaluateFirewall(observation.Services, observation.Firewall),
        ];

        return new CircleFilesReadinessReport(
            CircleFilesReadinessProviders.WindowsSmb311,
            Aggregate(checks),
            checks);
    }

    private static CircleFilesReadinessCheck EvaluatePlatform(WindowsSystemObservation? value)
    {
        if (value?.BuildNumber is null || string.IsNullOrWhiteSpace(value.InstallationType))
        {
            return Unknown("windows-platform", "windows_version_unknown", "Windows did not report a recognized product version.");
        }

        if (value.BuildNumber < 26100)
        {
            return NotReady("windows-platform", "windows_version_unsupported", "Windows 11 24H2 or Windows Server 2025 is required.");
        }

        if (value.InstallationType is not ("Client" or "Server" or "Server Core"))
        {
            return NotReady("windows-platform", "windows_edition_unsupported", "This Windows edition is not approved for SMB 3.1.1 hosting.");
        }

        return Ready("windows-platform", "windows_platform_supported", "This Windows version supports the required SMB hosting controls.");
    }

    private static CircleFilesReadinessCheck EvaluateSmbServer(
        WindowsServiceObservation? services,
        WindowsSmbServerObservation? server)
    {
        if (string.IsNullOrWhiteSpace(services?.LanmanServer))
        {
            return Unknown("smb-server", "smb_server_state_unknown", "Windows did not report the SMB server service state.");
        }

        if (!string.Equals(services.LanmanServer, "Running", StringComparison.Ordinal))
        {
            return IsKnownServiceState(services.LanmanServer)
                ? NotReady("smb-server", "smb_server_unavailable", "The Windows SMB server service is not running.")
                : Unknown("smb-server", "smb_server_state_unknown", "Windows reported an unrecognized SMB server service state.");
        }

        return server?.EnableSmb2Protocol switch
        {
            true => Ready("smb-server", "smb_server_available", "The Windows SMB 2/3 server is available."),
            false => NotReady("smb-server", "smb2_disabled", "SMB 2/3 hosting is disabled."),
            null => Unknown("smb-server", "smb2_state_unknown", "Windows did not report whether SMB 2/3 hosting is enabled."),
        };
    }

    private static CircleFilesReadinessCheck EvaluateDialect(WindowsSmbServerObservation? server) =>
        server?.MaximumDialect switch
        {
            WindowsSmbDialect.Smb311 or WindowsSmbDialect.NoLimit =>
                Ready("smb-dialect", "smb311_available", "The SMB server can negotiate SMB 3.1.1."),
            WindowsSmbDialect.Smb202 or WindowsSmbDialect.Smb210 or WindowsSmbDialect.Smb300 or WindowsSmbDialect.Smb302 =>
                NotReady("smb-dialect", "smb311_unavailable", "The SMB server maximum dialect is below SMB 3.1.1."),
            _ => Unknown("smb-dialect", "smb_dialect_unknown", "Windows did not report a recognized maximum SMB dialect."),
        };

    private static CircleFilesReadinessCheck EvaluateSmb1(WindowsSmbServerObservation? server) =>
        server?.EnableSmb1Protocol switch
        {
            false => Ready("smb1", "smb1_disabled", "SMB 1 hosting is disabled."),
            true => NotReady("smb1", "smb1_enabled", "SMB 1 hosting must be disabled."),
            null => Unknown("smb1", "smb1_state_unknown", "Windows did not report whether SMB 1 hosting is enabled."),
        };

    private static CircleFilesReadinessCheck EvaluateGuestAccess(WindowsSmbClientObservation? client) =>
        client?.EnableInsecureGuestLogons switch
        {
            false => Ready("guest-access", "insecure_guest_disabled", "Insecure SMB guest logons are disabled."),
            true => NotReady("guest-access", "insecure_guest_enabled", "Insecure SMB guest logons must be disabled."),
            null => Unknown("guest-access", "insecure_guest_state_unknown", "Windows did not report the insecure guest logon policy."),
        };

    private static CircleFilesReadinessCheck EvaluateSigning(WindowsSmbServerObservation? server) =>
        server?.RequireSecuritySignature switch
        {
            true => Ready("signing", "smb_signing_required", "The SMB server requires signing."),
            false => NotReady("signing", "smb_signing_not_required", "The SMB server must require signing."),
            null => Unknown("signing", "smb_signing_state_unknown", "Windows did not report the SMB signing requirement."),
        };

    private static CircleFilesReadinessCheck EvaluateEncryption(WindowsSmbServerObservation? server)
    {
        if (server?.RejectUnencryptedAccess is false)
        {
            return NotReady("encryption", "unencrypted_access_accepted", "The SMB server must reject unencrypted access to encrypted shares.");
        }

        if (server?.ShareEncryptionSupported is false)
        {
            return NotReady("encryption", "share_encryption_unavailable", "This Windows configuration cannot require encryption on a new SMB share.");
        }

        if (server?.EncryptionCiphers is { } ciphers
            && !ciphers.Any(cipher => cipher is "AES_128_GCM" or "AES_256_GCM"))
        {
            return NotReady("encryption", "smb311_encryption_cipher_unavailable", "No approved SMB 3.1.1 encryption cipher is available.");
        }

        if (server?.RejectUnencryptedAccess is null
            || server.ShareEncryptionSupported is null
            || server.EncryptionCiphers is null)
        {
            return Unknown("encryption", "smb_encryption_state_unknown", "Windows did not report all required SMB encryption controls.");
        }

        return Ready("encryption", "smb_encryption_enforceable", "A new SMB share can require SMB 3.1.1 encryption and reject unencrypted access.");
    }

    private static CircleFilesReadinessCheck EvaluatePrivateNetwork(WindowsNetworkObservation? network) =>
        network?.ConnectedPrivateProfiles switch
        {
            > 0 => Ready("private-network", "private_network_available", "A connected private Windows network profile is available."),
            0 => NotReady("private-network", "private_network_unavailable", "No connected private Windows network profile is available."),
            _ => Unknown("private-network", "network_profile_unknown", "Windows did not report the connected private network scope."),
        };

    private static CircleFilesReadinessCheck EvaluateFirewall(
        WindowsServiceObservation? services,
        WindowsFirewallObservation? firewall)
    {
        if (string.IsNullOrWhiteSpace(services?.WindowsFirewall))
        {
            return Unknown("firewall-scope", "windows_firewall_state_unknown", "Windows did not report the firewall service state.");
        }

        if (!string.Equals(services.WindowsFirewall, "Running", StringComparison.Ordinal))
        {
            return IsKnownServiceState(services.WindowsFirewall)
                ? NotReady("firewall-scope", "windows_firewall_unavailable", "Windows Firewall is not running.")
                : Unknown("firewall-scope", "windows_firewall_state_unknown", "Windows reported an unrecognized firewall service state.");
        }

        if (firewall?.PrivateEnabled is false)
        {
            return NotReady("firewall-scope", "private_firewall_disabled", "The private Windows Firewall profile must be enabled.");
        }

        if (firewall?.PublicEnabled is false)
        {
            return NotReady("firewall-scope", "public_firewall_disabled", "The public Windows Firewall profile must be enabled.");
        }

        if (firewall?.PrivateDefaultInboundAction is { } privateAction
            && !string.Equals(privateAction, "Block", StringComparison.Ordinal))
        {
            return NotReady("firewall-scope", "private_inbound_not_blocked", "The private Windows Firewall profile must block inbound traffic by default.");
        }

        if (firewall?.PublicDefaultInboundAction is { } publicAction
            && !string.Equals(publicAction, "Block", StringComparison.Ordinal))
        {
            return NotReady("firewall-scope", "public_inbound_not_blocked", "The public Windows Firewall profile must block inbound traffic by default.");
        }

        if (firewall?.PrivateEnabled is null
            || firewall.PublicEnabled is null
            || firewall.PrivateDefaultInboundAction is null
            || firewall.PublicDefaultInboundAction is null)
        {
            return Unknown("firewall-scope", "firewall_scope_unknown", "Windows did not report all required private and public firewall controls.");
        }

        return Ready("firewall-scope", "private_firewall_scope_enforceable", "Windows Firewall can allow SMB only on the private profile while public inbound traffic remains blocked.");
    }

    private static CircleFilesReadinessReport InspectionFailed()
    {
        var checks = new[]
        {
            "windows-platform",
            "smb-server",
            "smb-dialect",
            "smb1",
            "guest-access",
            "signing",
            "encryption",
            "private-network",
            "firewall-scope",
        }.Select(id => Unknown(id, "inspection_failed", "Windows readiness inspection did not complete.")).ToArray();
        return new CircleFilesReadinessReport(
            CircleFilesReadinessProviders.WindowsSmb311,
            CircleFilesReadinessStatus.Unknown,
            checks);
    }

    private static CircleFilesReadinessStatus Aggregate(
        IEnumerable<CircleFilesReadinessCheck> checks)
    {
        var statuses = checks.Select(check => check.Status).ToArray();
        if (statuses.Contains(CircleFilesReadinessStatus.NotReady))
        {
            return CircleFilesReadinessStatus.NotReady;
        }

        return statuses.Contains(CircleFilesReadinessStatus.Unknown)
            ? CircleFilesReadinessStatus.Unknown
            : CircleFilesReadinessStatus.Ready;
    }

    private static bool IsKnownServiceState(string value) => value is
        "Stopped" or "StartPending" or "StopPending" or "ContinuePending" or "PausePending" or "Paused";

    private static CircleFilesReadinessCheck Ready(string id, string code, string summary) =>
        new(id, CircleFilesReadinessStatus.Ready, code, summary);

    private static CircleFilesReadinessCheck NotReady(string id, string code, string summary) =>
        new(id, CircleFilesReadinessStatus.NotReady, code, summary);

    private static CircleFilesReadinessCheck Unknown(string id, string code, string summary) =>
        new(id, CircleFilesReadinessStatus.Unknown, code, summary);

    private static bool IsExpectedInspectionFailure(Exception exception) => exception is
        WindowsInspectionException or
        Win32Exception or
        IOException or
        UnauthorizedAccessException or
        InvalidOperationException or
        SecurityException or
        JsonException;
}

internal enum WindowsSmbDialect
{
    Unknown,
    NoLimit,
    Smb202,
    Smb210,
    Smb300,
    Smb302,
    Smb311,
}

internal sealed record WindowsSystemObservation(int? BuildNumber, string? InstallationType);

internal sealed record WindowsServiceObservation(string? LanmanServer, string? WindowsFirewall);

internal sealed record WindowsSmbServerObservation(
    bool? EnableSmb1Protocol,
    bool? EnableSmb2Protocol,
    WindowsSmbDialect? MaximumDialect,
    bool? RequireSecuritySignature,
    bool? RejectUnencryptedAccess,
    bool? ShareEncryptionSupported,
    IReadOnlyList<string>? EncryptionCiphers);

internal sealed record WindowsSmbClientObservation(bool? EnableInsecureGuestLogons);

internal sealed record WindowsNetworkObservation(int? ConnectedPrivateProfiles);

internal sealed record WindowsFirewallObservation(
    bool? PrivateEnabled,
    string? PrivateDefaultInboundAction,
    bool? PublicEnabled,
    string? PublicDefaultInboundAction);

internal sealed record WindowsSmbReadinessObservation(
    WindowsSystemObservation? System,
    WindowsServiceObservation? Services,
    WindowsSmbServerObservation? SmbServer,
    WindowsSmbClientObservation? SmbClient,
    WindowsNetworkObservation? Network,
    WindowsFirewallObservation? Firewall);

internal static class WindowsSmbReadinessJson
{
    internal static WindowsSmbReadinessObservation Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The readiness response must be one object.");
        }

        var root = document.RootElement;
        return new WindowsSmbReadinessObservation(
            ParseSystem(OptionalObject(root, "System")),
            ParseServices(OptionalObject(root, "Services")),
            ParseSmbServer(OptionalObject(root, "SmbServer")),
            ParseSmbClient(OptionalObject(root, "SmbClient")),
            ParseNetwork(OptionalObject(root, "Network")),
            ParseFirewall(OptionalObject(root, "Firewall")));
    }

    private static WindowsSystemObservation? ParseSystem(JsonElement? element) => element is null
        ? null
        : new WindowsSystemObservation(
            OptionalInt32(element.Value, "BuildNumber"),
            OptionalString(element.Value, "InstallationType"));

    private static WindowsServiceObservation? ParseServices(JsonElement? element) => element is null
        ? null
        : new WindowsServiceObservation(
            OptionalString(element.Value, "LanmanServer"),
            OptionalString(element.Value, "WindowsFirewall"));

    private static WindowsSmbServerObservation? ParseSmbServer(JsonElement? element) => element is null
        ? null
        : new WindowsSmbServerObservation(
            OptionalBoolean(element.Value, "EnableSMB1Protocol"),
            OptionalBoolean(element.Value, "EnableSMB2Protocol"),
            ParseDialect(OptionalString(element.Value, "Smb2DialectMax")),
            OptionalBoolean(element.Value, "RequireSecuritySignature"),
            OptionalBoolean(element.Value, "RejectUnencryptedAccess"),
            OptionalBoolean(element.Value, "ShareEncryptionSupported"),
            OptionalStringArray(element.Value, "EncryptionCiphers"));

    private static WindowsSmbClientObservation? ParseSmbClient(JsonElement? element) => element is null
        ? null
        : new WindowsSmbClientObservation(
            OptionalBoolean(element.Value, "EnableInsecureGuestLogons"));

    private static WindowsNetworkObservation? ParseNetwork(JsonElement? element) => element is null
        ? null
        : new WindowsNetworkObservation(
            OptionalInt32(element.Value, "ConnectedPrivateProfiles"));

    private static WindowsFirewallObservation? ParseFirewall(JsonElement? element) => element is null
        ? null
        : new WindowsFirewallObservation(
            OptionalBoolean(element.Value, "PrivateEnabled"),
            OptionalString(element.Value, "PrivateDefaultInboundAction"),
            OptionalBoolean(element.Value, "PublicEnabled"),
            OptionalString(element.Value, "PublicDefaultInboundAction"));

    private static JsonElement? OptionalObject(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Object
            ? value
            : throw new JsonException($"{propertyName} must be an object.");
    }

    private static bool? OptionalBoolean(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new JsonException($"{propertyName} must be a Boolean."),
        };
    }

    private static int? OptionalInt32(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : throw new JsonException($"{propertyName} must be a 32-bit integer.");
    }

    private static string? OptionalString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw new JsonException($"{propertyName} must be a string.");
    }

    private static IReadOnlyList<string>? OptionalStringArray(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"{propertyName} must be an array.");
        }

        return value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
            ? item.GetString()!
            : throw new JsonException($"{propertyName} must contain only strings.")).ToArray();
    }

    private static WindowsSmbDialect? ParseDialect(string? value) => value switch
    {
        null => null,
        "None" or "0" or "65535" or "65536" => WindowsSmbDialect.NoLimit,
        "SMB202" or "514" => WindowsSmbDialect.Smb202,
        "SMB210" or "528" => WindowsSmbDialect.Smb210,
        "SMB300" or "768" => WindowsSmbDialect.Smb300,
        "SMB302" or "770" => WindowsSmbDialect.Smb302,
        "SMB311" or "785" => WindowsSmbDialect.Smb311,
        _ => WindowsSmbDialect.Unknown,
    };
}
