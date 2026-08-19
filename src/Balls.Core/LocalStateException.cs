namespace Balls.Core;

public class LocalStateException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public sealed class LocalStateConflictException(string code, string message)
    : LocalStateException(code, message);

public sealed class UnsupportedLocalStateSchemaException(int foundVersion, int supportedVersion)
    : LocalStateException(
        "unsupported_state_schema",
        $"Local state schema {foundVersion} is newer than supported schema {supportedVersion}.")
{
    public int FoundVersion { get; } = foundVersion;

    public int SupportedVersion { get; } = supportedVersion;
}

public sealed class LocalStateOpenException(Exception innerException)
    : LocalStateException(
        "invalid_local_state",
        "Local state could not be opened. The existing database was left in place.",
        innerException);
