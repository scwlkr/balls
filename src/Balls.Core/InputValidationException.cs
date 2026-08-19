namespace Balls.Core;

public sealed class InputValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
