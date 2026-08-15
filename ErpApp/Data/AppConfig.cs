using System.Text.Json;

namespace ErpApp.Data;

/// <summary>
/// Simple settings loader - avoids pulling in Microsoft.Extensions.Configuration
/// so the project has a single NuGet dependency (Npgsql).
/// </summary>
public static class AppConfig
{
    public static string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Override the connection string (used by integration tests; handy for embedded scenarios too).</summary>
    public static void SetConnectionString(string connectionString) => ConnectionString = connectionString;

    public static string CompanyName { get; private set; } = "Company";
    public static string FinancialYear { get; private set; } = "";
    public static string CurrentUser { get; set; } = "admin";

    public static void SetCompanyName(string name) => CompanyName = name;
    public static void SetFinancialYear(string fy) => FinancialYear = fy;

    public static void Load()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("appsettings.json not found next to the executable.", path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        ConnectionString = root.GetProperty("ConnectionStrings").GetProperty("ErpDb").GetString() ?? "";

        if (root.TryGetProperty("Company", out var company))
        {
            CompanyName = company.GetProperty("Name").GetString() ?? CompanyName;
            FinancialYear = company.GetProperty("FinancialYear").GetString() ?? FinancialYear;
        }
    }
}
