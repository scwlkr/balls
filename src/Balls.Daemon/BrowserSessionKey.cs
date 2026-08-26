using System.Security.Cryptography;
using System.Text;
using Balls.Core;

namespace Balls.Daemon;

internal static class BrowserSessionKey
{
    internal static string Create(string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken) || sessionToken.Length > 1024)
        {
            throw new InputValidationException(
                "browser_session_required",
                "A valid browser session is required.");
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken)));
    }
}
