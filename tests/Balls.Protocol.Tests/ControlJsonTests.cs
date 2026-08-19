using System.Text.Json;
using Balls.Protocol.Control.V1;

namespace Balls.Protocol.Tests;

[TestClass]
[TestCategory("Contract")]
public sealed class ControlJsonTests
{
    [TestMethod]
    public void Status_response_has_the_stable_v1_wire_shape()
    {
        var response = new StatusResponse(
            "0.1.0-alpha.1",
            1,
            new NodeResponse(
                "0198c2d8-b000-7000-8000-000000000001",
                "Alice-PC",
                new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero)));

        var json = JsonSerializer.Serialize(response, ControlJson.Options);

        Assert.AreEqual(
            "{\"productVersion\":\"0.1.0-alpha.1\",\"protocolVersion\":1,"
            + "\"node\":{\"id\":\"0198c2d8-b000-7000-8000-000000000001\","
            + "\"displayName\":\"Alice-PC\",\"createdAtUtc\":\"2026-08-19T12:00:00+00:00\"}}",
            json);
    }

    [TestMethod]
    public void V1_readers_ignore_additive_fields_from_a_newer_writer()
    {
        const string json =
            """
            {
              "productVersion": "0.1.0-alpha.1",
              "protocolVersion": 1,
              "node": {
                "id": "0198c2d8-b000-7000-8000-000000000001",
                "displayName": "Alice-PC",
                "createdAtUtc": "2026-08-19T12:00:00+00:00",
                "futureCapability": true
              },
              "futureTopLevelField": "ignored"
            }
            """;

        var response = JsonSerializer.Deserialize<StatusResponse>(json, ControlJson.Options);

        Assert.IsNotNull(response);
        Assert.AreEqual(1, response.ProtocolVersion);
        Assert.AreEqual("Alice-PC", response.Node.DisplayName);
    }
}
