using System.Text.Json;
using System.Text.Json.Serialization;

namespace Balls.Protocol.Control.V1;

public static class ControlJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }
}
