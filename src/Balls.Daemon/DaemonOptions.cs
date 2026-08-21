namespace Balls.Daemon;

public sealed record DaemonOptions(
    string DataDirectory,
    string LocalControlEndpoint,
    string NodeDisplayName,
    string? AdmissionListenEndpoint = null,
    string? MessageListenEndpoint = null);
