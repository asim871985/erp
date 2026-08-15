using System.Security.Cryptography;
using System.Text;
using ErpApp.Data;

namespace ErpApp.IntegrationTests;

/// <summary>
/// Login-flow integration tests against a scratch database (own collection /
/// own scratch DB, since the seeded admin row gets migrated in place). They
/// replicate exactly what LoginForm.BtnLogin_Click does — fetch the stored
/// hash, verify via PasswordHelper, reject inactive accounts, and re-hash with
/// BCrypt on the spot when the stored value is legacy — driven through the real
/// PasswordHelper + DbHelper code.
/// </summary>
[Collection("LoginFlow")]
public class LoginFlowTests
{
    // Replicates the old pre-BCrypt algorithm (SHA-256 with the fixed "ErpApp-v1-"
    // salt, hex-encoded) so we can build authentic legacy hashes.
    private static string LegacySha256(string plainText)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("ErpApp-v1-" + plainText));
        return Convert.ToHexString(bytes);
    }

    private static int InsertUser(string username, string passwordHash, bool active = true) =>
        Convert.ToInt32(DbHelper.ExecuteScalar(@"
            INSERT INTO users_master (username, password_hash, full_name, role, active)
            VALUES (@u, @h, 'Integration Test', 'User', @a) RETURNING user_id",
            new Dictionary<string, object?>
            {
                ["u"] = username, ["h"] = passwordHash, ["a"] = active
            }));

    /// <summary>
    /// The exact flow LoginForm.BtnLogin_Click runs (minus the UI): fetch the
    /// user, verify the password, check the active flag, and — only on success —
    /// migrate a legacy hash to BCrypt in place. Returns what the form sees.
    /// </summary>
    private static (bool Ok, bool Active, string StoredHash) TryLogin(string username, string password)
    {
        var t = DbHelper.ExecuteQuery("SELECT user_id, password_hash, active FROM users_master WHERE username=@u",
            new Dictionary<string, object?> { ["u"] = username });
        if (t.Rows.Count == 0) return (false, false, "");
        var row = t.Rows[0];
        int userId = (int)row["user_id"];
        string storedHash = row["password_hash"].ToString() ?? "";
        bool active = row["active"] is bool b && b;

        bool ok = PasswordHelper.Verify(password, storedHash);
        if (ok && active && PasswordHelper.NeedsRehash(storedHash))
        {
            DbHelper.ExecuteNonQuery("UPDATE users_master SET password_hash=@hash WHERE user_id=@id",
                new Dictionary<string, object?> { ["hash"] = PasswordHelper.Hash(password), ["id"] = userId });
            storedHash = DbHelper.ExecuteScalar("SELECT password_hash FROM users_master WHERE user_id=@id",
                new Dictionary<string, object?> { ["id"] = userId })?.ToString() ?? "";
        }
        return (ok, active, storedHash);
    }

    // ---- Fresh install: the seeded admin row is plain text and self-migrates ----

    [Fact]
    public void SeededAdmin_PlainTextSeed_MigratesToBcryptOnLogin()
    {
        // schema.sql seeds users_master with ('admin', 'admin', ...) — plain text
        var before = DbHelper.ExecuteScalar("SELECT password_hash FROM users_master WHERE username='admin'")?.ToString();
        Assert.Equal("admin", before); // the exact fresh-install state
        Assert.True(PasswordHelper.NeedsRehash(before!));

        var login = TryLogin("admin", "admin");
        Assert.True(login.Ok);
        Assert.True(login.Active);

        // the login re-hashed the stored value to BCrypt on the spot
        Assert.StartsWith("$2", login.StoredHash);
        Assert.False(PasswordHelper.NeedsRehash(login.StoredHash));
        Assert.True(PasswordHelper.Verify("admin", login.StoredHash));

        // and the migrated account still works normally
        Assert.True(TryLogin("admin", "admin").Ok);
        Assert.False(TryLogin("admin", "wrong").Ok);
    }

    // ---- Other legacy users migrate the same way -------------------------------

    [Fact]
    public void PlainTextUser_MigratesOnLogin()
    {
        string user = "seeduser_" + Guid.NewGuid().ToString("N")[..8];
        InsertUser(user, "seedpass");

        var login = TryLogin(user, "seedpass");
        Assert.True(login.Ok);
        Assert.StartsWith("$2", login.StoredHash);
        Assert.True(PasswordHelper.Verify("seedpass", login.StoredHash));
    }

    [Fact]
    public void LegacySha256User_MigratesOnLogin()
    {
        string user = "legacy_" + Guid.NewGuid().ToString("N")[..8];
        InsertUser(user, LegacySha256("legacy-pass")); // stored by an old build

        var login = TryLogin(user, "legacy-pass");
        Assert.True(login.Ok);
        Assert.StartsWith("$2", login.StoredHash);
        Assert.True(PasswordHelper.Verify("legacy-pass", login.StoredHash));
        Assert.False(PasswordHelper.Verify("nope", login.StoredHash));
    }

    // ---- Blocked logins ---------------------------------------------------------

    [Fact]
    public void InactiveUser_CorrectPassword_StillBlocked_AndNotMigrated()
    {
        string user = "inactive_" + Guid.NewGuid().ToString("N")[..8];
        string bcrypt = PasswordHelper.Hash("right");
        InsertUser(user, bcrypt, active: false);

        var login = TryLogin(user, "right");
        Assert.True(login.Ok);        // password is correct…
        Assert.False(login.Active);   // …but the account is deactivated (form shows the message)
        Assert.Equal(bcrypt, login.StoredHash); // and nothing was migrated
    }

    [Fact]
    public void WrongPassword_NoMigrationAndNoAccess()
    {
        string user = "wrong_" + Guid.NewGuid().ToString("N")[..8];
        InsertUser(user, "right"); // plain text on purpose — must stay untouched

        var login = TryLogin(user, "nope");
        Assert.False(login.Ok);
        Assert.Equal("right", login.StoredHash); // legacy hash left exactly as-is
    }

    [Fact]
    public void UnknownUser_CannotLogIn()
    {
        var login = TryLogin("does_not_exist_" + Guid.NewGuid().ToString("N")[..8], "x");
        Assert.False(login.Ok);
        Assert.False(login.Active);
    }

    // ---- Post-login audit log -----------------------------------------------------

    [Fact]
    public void SuccessfulLogin_WritesLoggedInAuditRow()
    {
        string user = "audit_" + Guid.NewGuid().ToString("N")[..8];
        InsertUser(user, PasswordHelper.Hash("pw"));

        Assert.True(TryLogin(user, "pw").Ok);
        AppConfig.CurrentUser = user;
        DbHelper.LogAction("Logged in"); // what LoginForm calls after a successful login

        int rows = Convert.ToInt32(DbHelper.ExecuteScalar(
            "SELECT COUNT(*) FROM database_log WHERE username=@u AND action='Logged in'",
            new Dictionary<string, object?> { ["u"] = user }));
        Assert.Equal(1, rows);
    }
}

[CollectionDefinition("LoginFlow")]
public class LoginFlowCollection : ICollectionFixture<DatabaseFixture> { }
