namespace CasePlanner.Data;

public static class DatabaseProviders
{
    public const string Sqlite = "Sqlite";
    public const string SqlServer = "SqlServer";
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = DatabaseProviders.Sqlite;
    public string? ConnectionString { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 30;

    public bool IsSqlServer => Provider.Equals(DatabaseProviders.SqlServer, StringComparison.OrdinalIgnoreCase);
}
