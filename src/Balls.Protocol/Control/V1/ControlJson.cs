using System.Text.Json;
using System.Text.Json.Serialization;

namespace Balls.Protocol.Control.V1;

public static class ControlJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };
}
