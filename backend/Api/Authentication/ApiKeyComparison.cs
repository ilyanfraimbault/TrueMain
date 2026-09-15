using System.Security.Cryptography;
using System.Text;

namespace TrueMain.Authentication;

/// <summary>Constant-time comparison shared by the two API-key schemes.</summary>
internal static class ApiKeyComparison
{
    /// <summary>
    /// Whether <paramref name="provided"/> is <paramref name="configured"/>. Both sides
    /// are hashed first so <see cref="CryptographicOperations.FixedTimeEquals"/> always
    /// compares same-length spans: comparing the raw strings would return early on a
    /// length mismatch and reopen the timing channel the constant-time compare closes.
    /// </summary>
    public static bool Matches(string provided, string configured)
    {
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        return CryptographicOperations.FixedTimeEquals(providedHash, configuredHash);
    }
}
