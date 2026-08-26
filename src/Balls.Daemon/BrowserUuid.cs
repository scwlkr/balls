namespace Balls.Daemon;

internal static class BrowserUuid
{
    internal static bool TryParse(string? value, out Guid parsed) =>
        Guid.TryParseExact(value, "D", out parsed)
        && parsed != Guid.Empty
        && string.Equals(value, parsed.ToString("D"), StringComparison.Ordinal);
}
