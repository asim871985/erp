using System.Security.Cryptography;
using System.Text;
using ErpApp.Data;

namespace ErpApp.Tests;

/// <summary>
/// Tests for <see cref="PasswordHelper"/>: BCrypt hashing, plus the two legacy
/// verification paths (old SHA-256-with-fixed-salt hashes and the plain-text
/// "admin" seed) that exist so upgrading the app never locks anyone out.
/// These are pure logic tests — no database required.
/// </summary>
public class PasswordHelperTests
{
    // ---- BCrypt hashing ------------------------------------------------

    [Fact]
    public void Hash_ProducesBcryptFormat()
    {
        var hash = PasswordHelper.Hash("admin");
        Assert.StartsWith("$2", hash); // $2a$ / $2b$ / $2y$
    }

    [Fact]
    public void Hash_SaltsPerCall_TwoHashesOfSamePasswordDiffer()
    {
        var a = PasswordHelper.Hash("admin");
        var b = PasswordHelper.Hash("admin");
        Assert.NotEqual(a, b); // BCrypt uses a random per-password salt
    }

    [Fact]
    public void RoundTrip_HashThenVerify_Succeeds()
    {
        foreach (var password in new[] { "admin", "P@ssw0rd!", "a", new string('x', 72) })
        {
            Assert.True(PasswordHelper.Verify(password, PasswordHelper.Hash(password)));
        }
    }

    [Fact]
    public void Verify_BcryptHash_WrongPassword_ReturnsFalse()
    {
        var hash = PasswordHelper.Hash("admin");
        Assert.False(PasswordHelper.Verify("wrong", hash));
    }

    [Fact]
    public void Verify_BcryptHash_IsCaseSensitive()
    {
        var hash = PasswordHelper.Hash("admin");
        Assert.False(PasswordHelper.Verify("ADMIN", hash));
    }

    // ---- Legacy path 1: SHA-256 with fixed salt (hashes written by earlier builds) --

    // Replicates the pre-BCrypt algorithm ("ErpApp-v1-" salt + SHA-256, hex-encoded)
    // so tests can build the exact kind of stored hash old builds produced.
    private static string LegacySha256(string plainText)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("ErpApp-v1-" + plainText));
        return Convert.ToHexString(bytes);
    }

    [Fact]
    public void Verify_LegacySha256Hash_CorrectPassword_Succeeds()
    {
        Assert.True(PasswordHelper.Verify("admin", LegacySha256("admin")));
    }

    [Fact]
    public void Verify_LegacySha256Hash_WrongPassword_ReturnsFalse()
    {
        Assert.False(PasswordHelper.Verify("wrong", LegacySha256("admin")));
    }

    // ---- Legacy path 2: plain-text seed ("admin") used by brand-new installs ----

    [Fact]
    public void Verify_PlainTextSeed_CorrectPassword_Succeeds()
    {
        Assert.True(PasswordHelper.Verify("admin", "admin"));
    }

    [Fact]
    public void Verify_PlainTextSeed_WrongPassword_ReturnsFalse()
    {
        Assert.False(PasswordHelper.Verify("wrong", "admin"));
    }

    // ---- Edge cases ------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Verify_EmptyOrNullStoredHash_ReturnsFalse(string? storedHash)
    {
        Assert.False(PasswordHelper.Verify("admin", storedHash!));
    }

    [Fact]
    public void Verify_EmptyPasswordAgainstBcrypt_ReturnsFalse()
    {
        Assert.False(PasswordHelper.Verify("", PasswordHelper.Hash("admin")));
    }

    // ---- NeedsRehash (drives the on-login migration) --------------------

    [Fact]
    public void NeedsRehash_BcryptHash_ReturnsFalse()
    {
        Assert.False(PasswordHelper.NeedsRehash(PasswordHelper.Hash("admin")));
    }

    [Fact]
    public void NeedsRehash_LegacySha256Hash_ReturnsTrue()
    {
        Assert.True(PasswordHelper.NeedsRehash(LegacySha256("admin")));
    }

    [Fact]
    public void NeedsRehash_PlainTextSeed_ReturnsTrue()
    {
        Assert.True(PasswordHelper.NeedsRehash("admin"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NeedsRehash_EmptyOrNull_ReturnsFalse(string? storedHash)
    {
        Assert.False(PasswordHelper.NeedsRehash(storedHash!));
    }

    [Fact]
    public void MigrationFlow_LegacyHashUpgradesToBcrypt_AndStillVerifies()
    {
        // What LoginForm does: verify against the legacy value, then replace it
        // with a BCrypt hash and check everything still works afterward.
        var legacy = LegacySha256("admin");
        Assert.True(PasswordHelper.Verify("admin", legacy));
        Assert.True(PasswordHelper.NeedsRehash(legacy));

        var upgraded = PasswordHelper.Hash("admin");
        Assert.False(PasswordHelper.NeedsRehash(upgraded));
        Assert.True(PasswordHelper.Verify("admin", upgraded));
        Assert.False(PasswordHelper.Verify("wrong", upgraded));
    }
}
