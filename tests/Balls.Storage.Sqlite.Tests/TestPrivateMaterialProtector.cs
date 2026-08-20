using System.Security.Cryptography;
using Balls.Core;

namespace Balls.Storage.Sqlite.Tests;

internal sealed class TestPrivateMaterialProtector : IPrivateMaterialProtector
{
    public static TestPrivateMaterialProtector Instance { get; } = new();

    public string Scheme => "tests-xor-v1";

    public byte[] Protect(ReadOnlySpan<byte> privateMaterial) => Transform(privateMaterial);

    public byte[] Unprotect(ReadOnlySpan<byte> protectedMaterial) => Transform(protectedMaterial);

    private static byte[] Transform(ReadOnlySpan<byte> input)
    {
        if (input.IsEmpty)
        {
            throw new CryptographicException("Test private material cannot be empty.");
        }

        var output = input.ToArray();
        for (var index = 0; index < output.Length; index++)
        {
            output[index] ^= 0xA5;
        }

        return output;
    }
}
