namespace Balls.Daemon;

public sealed record DaemonOptions(
    string DataDirectory,
    string PipeName,
    string NodeDisplayName);
