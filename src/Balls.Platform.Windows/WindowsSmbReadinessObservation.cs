using System.Text.Json;

namespace Balls.Platform.Windows;

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
    string? PublicDefaultInboundAction,
    int? PublicSmbInboundAllowRules);

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
            OptionalString(element.Value, "PublicDefaultInboundAction"),
            OptionalInt32(element.Value, "PublicSmbInboundAllowRules"));

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
