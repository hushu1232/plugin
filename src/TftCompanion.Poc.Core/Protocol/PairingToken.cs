using System.Security.Cryptography;
using System.Text;

namespace TftCompanion.Poc.Core.Protocol;

public static class PairingTokenGenerator
{
    public const int TokenByteLength = 32;

    public static string Generate()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public static class PairingTokenValidator
{
    // A 32-byte base64url value has 43 characters without padding.
    private const int ExpectedLength = 43;
    private const int ExpectedByteLength = 32;

    public static bool IsValid(string token)
    {
        if (token.Length != ExpectedLength)
        {
            return false;
        }

        foreach (char c in token)
        {
            if (!IsBase64UrlCharacter(c))
            {
                return false;
            }
        }

        // Canonical base64url check: decode, verify byte length, re-encode,
        // and require the re-encoded value to match the input verbatim.
        // This rejects tokens whose last character has non-zero padding bits
        // (e.g. 42 'A' + 'B') even though they have the correct length and
        // charset and decode to the same 32 bytes.
        if (!TryDecodeBase64Url(token, out byte[]? decoded) ||
            decoded is null ||
            decoded.Length != ExpectedByteLength)
        {
            return false;
        }

        string reencoded = EncodeBase64Url(decoded);
        return string.Equals(reencoded, token, StringComparison.Ordinal);
    }

    public static bool FixedTimeEquals(string expected, string? candidate)
    {
        if (candidate is null || !IsValid(expected) || !IsValid(candidate))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(candidate));
    }

    private static bool TryDecodeBase64Url(string token, out byte[]? decoded)
    {
        // base64url → base64: replace URL-safe chars and add padding.
        string base64 = token.Replace('-', '+').Replace('_', '/');
        int pad = base64.Length % 4;
        if (pad > 0)
        {
            base64 += new string('=', 4 - pad);
        }

        try
        {
            decoded = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            decoded = null;
            return false;
        }
    }

    private static string EncodeBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool IsBase64UrlCharacter(char c) =>
        (c >= '0' && c <= '9') ||
        (c >= 'a' && c <= 'z') ||
        (c >= 'A' && c <= 'Z') ||
        c is '-' or '_';
}
