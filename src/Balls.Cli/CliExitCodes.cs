namespace Balls.Cli;

public static class CliExitCodes
{
    public const int Success = 0;
    public const int UsageError = 2;
    public const int DaemonUnavailable = 3;
    public const int RequestRejected = 4;
    public const int PlatformUnsupported = 5;
}
