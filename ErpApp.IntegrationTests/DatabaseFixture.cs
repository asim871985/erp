using System.Diagnostics;
using ErpApp.Data;
using Npgsql;

namespace ErpApp.IntegrationTests;

/// <summary>
/// Creates a throwaway <c>erp_db_test</c> database from the real
/// <c>Database/schema.sql</c> before the collection runs, points the app's
/// DbHelper at it, and drops the database afterwards. The real erp_db is never
/// touched.
///
/// Requires PostgreSQL running locally and <c>psql</c> on PATH — the same
/// dependency as the app's own Data Backup/Restore tools. The connection
/// settings come from ErpApp/appsettings.json (linked into this project).
/// </summary>
public sealed class DatabaseFixture : IDisposable
{
    /// <summary>
    /// Unique per fixture instance so parallel test collections (each with their
    /// own fixture) never collide on the same database name.
    /// </summary>
    public string ScratchDb { get; } = "erp_db_test_" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>Connection string pointing at the scratch database.</summary>
    public string ConnectionString { get; }

    public DatabaseFixture()
    {
        AppConfig.Load(); // reads appsettings.json from the test output directory

        var (host, port, _, user, password) = DbHelper.GetConnectionParts();

        RunPsql(host, port, user, password, $"DROP DATABASE IF EXISTS {ScratchDb} WITH (FORCE)");
        RunPsql(host, port, user, password, $"CREATE DATABASE {ScratchDb}");

        var builder = new NpgsqlConnectionStringBuilder(AppConfig.ConnectionString) { Database = ScratchDb };
        ConnectionString = builder.ConnectionString;

        // schema.sql starts with psql-only preamble (DROP/CREATE erp_db, \c erp_db);
        // everything after the \c line is plain SQL, which psql loads into the scratch DB.
        string schemaPath = Path.Combine(AppContext.BaseDirectory, "schema.sql");
        var stripped = string.Join("\n",
            File.ReadAllLines(schemaPath).SkipWhile(l => !l.TrimStart().StartsWith("\\c")).Skip(1));
        string strippedPath = Path.Combine(Path.GetTempPath(), "erp_schema_for_tests.sql");
        File.WriteAllText(strippedPath, stripped);

        RunPsqlFile(host, port, user, password, ScratchDb, strippedPath);

        AppConfig.SetConnectionString(ConnectionString);
        AppConfig.CurrentUser = "admin";
    }

    private static void RunPsqlFile(string host, string port, string user, string password, string database, string filePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "psql",
            Arguments = $"-h {host} -p {port} -U {user} -d {database} -v ON_ERROR_STOP=1 -f \"{filePath}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        psi.Environment["PGPASSWORD"] = password;

        using var process = Process.Start(psi);
        if (process == null) throw new Exception("Could not start psql. Is it on PATH?");
        string stderr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new Exception($"psql failed ({process.ExitCode}): {stderr}");
    }

    private static void RunPsql(string host, string port, string user, string password, string command, string? database = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "psql",
            Arguments = $"-h {host} -p {port} -U {user} {(database == null ? "" : $"-d {database} ")}-v ON_ERROR_STOP=1 -c \"{command}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        psi.Environment["PGPASSWORD"] = password;

        using var process = Process.Start(psi);
        if (process == null) throw new Exception("Could not start psql. Is it on PATH?");
        string stderr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new Exception($"psql failed ({process.ExitCode}): {stderr}");
    }

    public void Dispose()
    {
        try
        {
            // Parse from the fixture's own connection string rather than the static
            // AppConfig (which another collection's fixture may have overwritten),
            // so this always drops the database this fixture created.
            var b = new NpgsqlConnectionStringBuilder(ConnectionString);
            RunPsql(b.Host ?? "localhost", b.Port.ToString(), b.Username ?? "postgres", b.Password ?? "",
                $"DROP DATABASE IF EXISTS {ScratchDb} WITH (FORCE)");
        }
        catch { /* best-effort cleanup */ }
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }
