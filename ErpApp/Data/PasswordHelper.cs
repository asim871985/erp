using System.Security.Cryptography;
using System.Text;

namespace ErpApp.Data;

/// <summary>
/// Password hashing for users_master.password_hash.
///
/// New hashes are BCrypt (via BCrypt.Net-Next) — a real KDF with a per-password
/// salt, suitable even if the app faces the public internet. Verification also
/// accepts two legacy formats so nobody gets locked out by an upgrade:
///   1. the old SHA-256-with-fixed-salt hashes written by earlier builds, and
///   2. the plain-text seed value ("admin") used by a brand-new install before
///      anyone has set a real password.
/// Call <see cref="NeedsRehash"/> after a successful legacy login and store the
/// re-hashed value so the account migrates to BCrypt on its next login.
/// </summary>
public static class PasswordHelper
{
    /// <summary>BCrypt work factor (4.2.0 default is 11 — 2^11 rounds).</summary>
    private const int WorkFactor = 11;

    public static string Hash(string plainText) => BCrypt.Net.BCrypt.HashPassword(plainText, WorkFactor);

    /// <summary>
    /// True when the plain-text password matches the stored value: BCrypt hash,
    /// legacy SHA-256 hash, or the plain-text "admin" seed.
    /// </summary>
    public static bool Verify(string plainText, string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return false;

        if (storedHash.StartsWith("$2", StringComparison.Ordinal)) // $2a$/$2b$/$2y$ — BCrypt
            return BCrypt.Net.BCrypt.Verify(plainText, storedHash);

        // Legacy: SHA-256 with a fixed salt, or the plain-text seed value.
        return string.Equals(LegacyHash(plainText), storedHash, StringComparison.OrdinalIgnoreCase)
               || string.Equals(plainText, storedHash, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when the stored value is not a BCrypt hash (legacy SHA-256 or the
    /// plain-text seed) and should be re-hashed with BCrypt on the next login.
    /// </summary>
    public static bool NeedsRehash(string storedHash) =>
        !string.IsNullOrEmpty(storedHash) && !storedHash.StartsWith("$2", StringComparison.Ordinal);

    private static string LegacyHash(string plainText)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("ErpApp-v1-" + plainText));
        return Convert.ToHexString(bytes);
    }
}
