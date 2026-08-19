namespace Balls.Protocol.Control.V1;

public static class ControlProtocol
{
    public const int Version = 1;
}

public static class ControlRoutes
{
    public const string BasePath = "/control/v1";
    public const string Status = BasePath + "/status";
    public const string Circles = BasePath + "/circles";
    public const string OpenApi = BasePath + "/openapi.json";

    public static string Circle(string circleId)
    {
        return $"{Circles}/{Uri.EscapeDataString(circleId)}";
    }

    public static string CircleMembers(string circleId)
    {
        return $"{Circle(circleId)}/members";
    }

    public static string CircleNodes(string circleId)
    {
        return $"{Circle(circleId)}/nodes";
    }
}
